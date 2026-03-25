using BlazorDataOrchestrator.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Orchestrates the full LLM build-error fix loop:
///   1. Detect errors from the BuildErrorStore
///   2. Gather context using ContextGatherer (Roslyn analysis)
///   3. Build a structured prompt using PromptBuilder
///   4. Call the LLM for a fix
///   5. Apply the fix and rebuild
///   6. Classify failures and record in FixAttemptStore
///   7. Auto-populate the negative-example bank from repeated failures
/// 
/// Supports configurable max retry attempts with expanding context on each retry.
/// Emits OpenTelemetry-compatible metrics for Aspire dashboard visibility.
/// </summary>
public class LlmFixOrchestrator
{
    private readonly BuildErrorStore _errorStore;
    private readonly FixAttemptStore _attemptStore;
    private readonly ContextGatherer _contextGatherer;
    private readonly PromptBuilder _promptBuilder;
    private readonly RootCauseClassifier _classifier;
    private readonly ILogger<LlmFixOrchestrator> _logger;

    // OpenTelemetry metrics
    private static readonly Meter s_meter = new("BlazorDataOrchestrator.LlmFix", "1.0.0");
    private static readonly Counter<long> s_attemptCounter = s_meter.CreateCounter<long>(
        "llm.fix_attempt.total", description: "Total LLM fix attempts");
    private static readonly Counter<long> s_rootCauseCounter = s_meter.CreateCounter<long>(
        "llm.fix_attempt.root_cause", description: "Root cause classifications for failed fixes");
    private static readonly Histogram<double> s_fixDuration = s_meter.CreateHistogram<double>(
        "llm.fix_attempt.duration_ms", "ms", "Duration of a fix attempt");

    /// <summary>
    /// Maximum number of retry attempts before escalating to the developer.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Root path of the solution for context gathering.
    /// </summary>
    public string SolutionRootPath { get; set; } = "";

    public LlmFixOrchestrator(
        BuildErrorStore errorStore,
        FixAttemptStore attemptStore,
        ContextGatherer contextGatherer,
        PromptBuilder promptBuilder,
        RootCauseClassifier classifier,
        ILogger<LlmFixOrchestrator> logger)
    {
        _errorStore = errorStore;
        _attemptStore = attemptStore;
        _contextGatherer = contextGatherer;
        _promptBuilder = promptBuilder;
        _classifier = classifier;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to fix a build error using the LLM.
    /// Returns the fix result including whether the fix was successful and any generated code.
    /// </summary>
    /// <param name="error">The build error to fix.</param>
    /// <param name="chatClient">The LLM chat client to use for generating fixes.</param>
    /// <param name="rebuildFunc">
    /// A function that applies the fixed code and rebuilds. 
    /// Receives the fixed source code and returns (success, residualErrors).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the fix attempt chain.</returns>
    public async Task<FixResult> AttemptFixAsync(
        BuildError error,
        IChatClient chatClient,
        Func<string, Task<(bool Success, List<BuildError> Errors)>> rebuildFunc,
        CancellationToken cancellationToken = default)
    {
        var overallStopwatch = Stopwatch.StartNew();
        var allAttempts = new List<FixAttempt>();
        BuildErrorContext? context = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            _logger.LogInformation(
                "Fix attempt {Attempt}/{Max} for {ErrorCode} at {File}:{Line}",
                attempt, MaxAttempts, error.ErrorCode, error.FilePath, error.Line);

            var attemptStopwatch = Stopwatch.StartNew();

            try
            {
                // 1. Gather context (expand on retries)
                var negativeExamples = _attemptStore.GetNegativeExamples(error.ErrorCode);

                if (context == null)
                {
                    context = _contextGatherer.GatherContext(error, SolutionRootPath, negativeExamples);
                }
                else
                {
                    // Expand context on retry
                    context = _contextGatherer.ExpandContext(context, SolutionRootPath, attempt - 1);
                }

                // 2. Build the prompt
                var prompt = _promptBuilder.BuildPrompt(context);

                // 3. Call the LLM
                var llmResponse = await CallLlmAsync(chatClient, prompt, cancellationToken);

                // 4. Extract the fixed code from the response
                var fixedCode = ExtractCodeFromResponse(llmResponse);

                if (string.IsNullOrWhiteSpace(fixedCode))
                {
                    _logger.LogWarning("LLM returned no extractable code block in attempt {Attempt}", attempt);

                    var noCodeAttempt = new FixAttempt(
                        Guid.NewGuid(), error, prompt, llmResponse,
                        false, null, RootCauseCategory.PromptAmbiguity,
                        DateTimeOffset.UtcNow);
                    _attemptStore.Record(noCodeAttempt);
                    allAttempts.Add(noCodeAttempt);

                    RecordMetrics("no_code", RootCauseCategory.PromptAmbiguity, attemptStopwatch.ElapsedMilliseconds);
                    continue;
                }

                // 5. Apply fix and rebuild
                var (rebuildSuccess, residualErrors) = await rebuildFunc(fixedCode);

                // 6. Record the attempt
                BuildError? residualError = residualErrors.FirstOrDefault();
                RootCauseCategory rootCause = RootCauseCategory.Unknown;

                if (!rebuildSuccess && residualError != null)
                {
                    // Build set of symbols from the provided API surface
                    var providedSymbols = context.RelatedTypes
                        .SelectMany(t => ExtractSymbolNames(t.PublicMemberSummary))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    rootCause = _classifier.Classify(error, residualError, llmResponse, providedSymbols);

                    // Auto-populate negative examples from hallucinated symbols
                    if (rootCause == RootCauseCategory.HallucinatedApi)
                    {
                        var referencedSymbols = _classifier.ExtractReferencedSymbols(llmResponse);
                        foreach (var sym in referencedSymbols.Where(s => !providedSymbols.Contains(s)))
                        {
                            _attemptStore.AddNegativeExample(new NegativeExample(
                                error.ErrorCode, sym, "(see API surface)", DateTimeOffset.UtcNow));
                        }
                    }
                }

                var fixAttempt = new FixAttempt(
                    Guid.NewGuid(), error, prompt, llmResponse,
                    rebuildSuccess, residualError, rootCause,
                    DateTimeOffset.UtcNow);
                _attemptStore.Record(fixAttempt);
                allAttempts.Add(fixAttempt);

                var outcome = rebuildSuccess ? "success" : 
                    (residualError?.ErrorCode == error.ErrorCode ? "same_error" : "new_error");
                RecordMetrics(outcome, rootCause, attemptStopwatch.ElapsedMilliseconds);

                if (rebuildSuccess)
                {
                    _logger.LogInformation("Fix succeeded on attempt {Attempt}", attempt);
                    overallStopwatch.Stop();

                    return new FixResult
                    {
                        Success = true,
                        FixedCode = fixedCode,
                        Attempts = allAttempts,
                        TotalDurationMs = overallStopwatch.ElapsedMilliseconds
                    };
                }

                _logger.LogWarning(
                    "Fix failed on attempt {Attempt}: [{RootCause}] {ResidualError}",
                    attempt, rootCause, residualError?.Message ?? "unknown");

                // Update the error for the next retry if a different error appeared
                if (residualError != null && residualError.ErrorCode != error.ErrorCode)
                {
                    error = residualError;
                    context = null; // Force re-gathering context for the new error
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during fix attempt {Attempt}", attempt);

                var exceptionAttempt = new FixAttempt(
                    Guid.NewGuid(), error, "", ex.Message,
                    false, null, RootCauseCategory.Unknown,
                    DateTimeOffset.UtcNow);
                _attemptStore.Record(exceptionAttempt);
                allAttempts.Add(exceptionAttempt);
            }
        }

        // All attempts exhausted — escalate to developer
        overallStopwatch.Stop();
        _logger.LogWarning(
            "All {Max} fix attempts exhausted for {ErrorCode} at {File}:{Line}. Escalating to developer.",
            MaxAttempts, error.ErrorCode, error.FilePath, error.Line);

        return new FixResult
        {
            Success = false,
            Attempts = allAttempts,
            TotalDurationMs = overallStopwatch.ElapsedMilliseconds,
            EscalationReport = BuildEscalationReport(error, allAttempts)
        };
    }

    /// <summary>
    /// Calls the LLM with the assembled prompt and returns the response text.
    /// </summary>
    private async Task<string> CallLlmAsync(
        IChatClient chatClient, string prompt, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var options = new ChatOptions
        {
            Temperature = 0.2f, // Low temperature for deterministic fixes
            MaxOutputTokens = 4096
        };

        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);

        return response.Text ?? "";
    }

    /// <summary>
    /// Extracts the C# code block from the LLM response.
    /// Expects a fenced code block (```csharp ... ```).
    /// </summary>
    private string ExtractCodeFromResponse(string response)
    {
        // Try to find a fenced C# code block
        var csharpPattern = new Regex(@"```csharp\s*\n([\s\S]*?)```", RegexOptions.Multiline);
        var match = csharpPattern.Match(response);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        // Try generic fenced code block
        var genericPattern = new Regex(@"```\s*\n([\s\S]*?)```", RegexOptions.Multiline);
        match = genericPattern.Match(response);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return "";
    }

    /// <summary>
    /// Extracts symbol names from a type summary string.
    /// </summary>
    private HashSet<string> ExtractSymbolNames(string summary)
    {
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pattern = new Regex(@"\b(\w+)\s*[\(\{;]");
        foreach (Match match in pattern.Matches(summary))
        {
            symbols.Add(match.Groups[1].Value);
        }
        return symbols;
    }

    /// <summary>
    /// Records OpenTelemetry metrics for the fix attempt.
    /// </summary>
    private void RecordMetrics(string outcome, RootCauseCategory rootCause, long durationMs)
    {
        s_attemptCounter.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

        if (outcome != "success" && rootCause != RootCauseCategory.Unknown)
        {
            s_rootCauseCounter.Add(1, new KeyValuePair<string, object?>("category", rootCause.ToString()));
        }

        s_fixDuration.Record(durationMs, new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>
    /// Builds a structured escalation report when all fix attempts are exhausted.
    /// </summary>
    private string BuildEscalationReport(BuildError error, List<FixAttempt> attempts)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# LLM Fix Escalation Report");
        sb.AppendLine();
        sb.AppendLine($"## Original Error");
        sb.AppendLine($"- Code: {error.ErrorCode}");
        sb.AppendLine($"- Message: {error.Message}");
        sb.AppendLine($"- File: {error.FilePath}");
        sb.AppendLine($"- Line: {error.Line}, Column: {error.Column}");
        sb.AppendLine();
        sb.AppendLine("## Fix Attempts");

        for (int i = 0; i < attempts.Count; i++)
        {
            var a = attempts[i];
            sb.AppendLine($"### Attempt {i + 1}");
            sb.AppendLine($"- Outcome: {(a.RebuildSucceeded ? "Success" : "Failed")}");
            sb.AppendLine($"- Root Cause: {a.RootCauseCategory}");
            if (a.ResidualError != null)
            {
                sb.AppendLine($"- Residual Error: [{a.ResidualError.ErrorCode}] {a.ResidualError.Message}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Metrics Summary");
        var metrics = _attemptStore.GetMetrics();
        sb.AppendLine($"- Overall fix rate: {metrics.FirstAttemptFixRate:P1}");
        sb.AppendLine($"- Total attempts recorded: {metrics.TotalAttempts}");

        return sb.ToString();
    }
}

/// <summary>
/// Result of an LLM fix attempt chain.
/// </summary>
public class FixResult
{
    /// <summary>Whether the fix was ultimately successful.</summary>
    public bool Success { get; init; }

    /// <summary>The fixed source code (if successful).</summary>
    public string? FixedCode { get; init; }

    /// <summary>All fix attempts made.</summary>
    public List<FixAttempt> Attempts { get; init; } = [];

    /// <summary>Total duration of all attempts in milliseconds.</summary>
    public long TotalDurationMs { get; init; }

    /// <summary>Escalation report if all attempts failed (for developer review).</summary>
    public string? EscalationReport { get; init; }
}

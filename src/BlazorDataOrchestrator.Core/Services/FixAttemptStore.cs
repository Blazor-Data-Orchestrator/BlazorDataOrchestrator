using System.Collections.Concurrent;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// In-memory store for LLM fix attempts. Provides runtime analysis, 
/// root-cause classification, and negative-example extraction.
/// No external database dependency — all data is held in memory.
/// </summary>
public class FixAttemptStore
{
    private readonly ConcurrentQueue<FixAttempt> _attempts = new();
    private readonly ConcurrentDictionary<string, List<NegativeExample>> _negativeExamples = new();
    private readonly int _maxAttempts;
    private readonly int _maxNegativeExamplesPerCode;

    public FixAttemptStore(int maxAttempts = 1000, int maxNegativeExamplesPerCode = 10)
    {
        _maxAttempts = maxAttempts;
        _maxNegativeExamplesPerCode = maxNegativeExamplesPerCode;
    }

    /// <summary>
    /// Records a fix attempt with its outcome.
    /// </summary>
    public void Record(FixAttempt attempt)
    {
        _attempts.Enqueue(attempt);

        while (_attempts.Count > _maxAttempts)
            _attempts.TryDequeue(out _);
    }

    /// <summary>
    /// Gets all recorded fix attempts, most recent first.
    /// </summary>
    public IReadOnlyList<FixAttempt> GetAll(int? limit = null)
    {
        var result = _attempts.Reverse().ToList();
        return limit.HasValue ? result.Take(limit.Value).ToList() : result;
    }

    /// <summary>
    /// Gets fix attempts for a specific error code.
    /// </summary>
    public IReadOnlyList<FixAttempt> GetByErrorCode(string errorCode)
    {
        return _attempts
            .Where(a => a.OriginalError.ErrorCode == errorCode)
            .OrderByDescending(a => a.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Computes summary metrics from the recorded attempts.
    /// </summary>
    public FixAttemptMetrics GetMetrics()
    {
        var all = _attempts.ToList();
        var total = all.Count;
        if (total == 0) return new FixAttemptMetrics();

        var succeeded = all.Count(a => a.RebuildSucceeded);
        var failed = total - succeeded;

        var rootCauseCounts = all
            .Where(a => !a.RebuildSucceeded && a.RootCauseCategory != RootCauseCategory.Unknown)
            .GroupBy(a => a.RootCauseCategory)
            .ToDictionary(g => g.Key, g => g.Count());

        return new FixAttemptMetrics
        {
            TotalAttempts = total,
            SuccessCount = succeeded,
            FailureCount = failed,
            FirstAttemptFixRate = total > 0
                ? (double)all.GroupBy(a => a.OriginalError.ErrorCode + a.OriginalError.FilePath + a.OriginalError.Line)
                      .Count(g => g.First().RebuildSucceeded) / 
                  all.GroupBy(a => a.OriginalError.ErrorCode + a.OriginalError.FilePath + a.OriginalError.Line).Count()
                : 0,
            RootCauseCounts = rootCauseCounts
        };
    }

    /// <summary>
    /// Adds a negative example learned from a failed fix attempt.
    /// Capped per error code to avoid prompt bloat.
    /// </summary>
    public void AddNegativeExample(NegativeExample example)
    {
        var examples = _negativeExamples.GetOrAdd(example.ErrorCode, _ => []);
        lock (examples)
        {
            // Check for duplicates
            if (examples.Any(e => e.BadSymbol == example.BadSymbol))
                return;

            examples.Add(example);

            // Evict oldest if over cap
            while (examples.Count > _maxNegativeExamplesPerCode)
                examples.RemoveAt(0);
        }
    }

    /// <summary>
    /// Gets negative examples for a given error code.
    /// </summary>
    public IReadOnlyList<NegativeExample> GetNegativeExamples(string errorCode)
    {
        if (_negativeExamples.TryGetValue(errorCode, out var examples))
        {
            lock (examples)
            {
                return examples.ToList();
            }
        }
        return [];
    }
}

/// <summary>
/// Aggregated metrics from the fix attempt store.
/// </summary>
public class FixAttemptMetrics
{
    public int TotalAttempts { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public double FirstAttemptFixRate { get; init; }
    public Dictionary<RootCauseCategory, int> RootCauseCounts { get; init; } = [];
}

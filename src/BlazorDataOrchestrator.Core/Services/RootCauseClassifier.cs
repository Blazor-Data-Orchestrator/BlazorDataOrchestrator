using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Classifies root causes of LLM fix failures using heuristic analysis.
/// Compares the original error, the LLM response, and the residual error 
/// to determine what went wrong.
/// </summary>
public class RootCauseClassifier
{
    /// <summary>
    /// Classifies the root cause of a failed fix attempt.
    /// </summary>
    /// <param name="originalError">The build error the LLM tried to fix.</param>
    /// <param name="residualError">The build error that remained after applying the fix (null if different error).</param>
    /// <param name="llmResponse">The full LLM response containing the proposed fix.</param>
    /// <param name="providedApiSurface">Set of symbols that were included in the prompt's API surface.</param>
    /// <returns>The classified root cause category.</returns>
    public RootCauseCategory Classify(
        BuildError originalError,
        BuildError? residualError,
        string llmResponse,
        HashSet<string>? providedApiSurface = null)
    {
        if (residualError == null)
            return RootCauseCategory.Unknown;

        // 1. Same error code + same location → Insufficient context or Hallucinated API
        if (residualError.ErrorCode == originalError.ErrorCode &&
            residualError.FilePath == originalError.FilePath &&
            residualError.Line == originalError.Line)
        {
            // Check for hallucinated API: LLM referenced a symbol not in the provided context
            if (providedApiSurface != null && ContainsUnprovidedSymbol(llmResponse, providedApiSurface))
                return RootCauseCategory.HallucinatedApi;

            return RootCauseCategory.InsufficientFileContext;
        }

        // 2. Same error code + different location → Incomplete fix
        if (residualError.ErrorCode == originalError.ErrorCode)
        {
            return RootCauseCategory.InsufficientFileContext;
        }

        // 3. CS0246 (type not found) → likely missing NuGet
        if (residualError.ErrorCode == "CS0246")
        {
            return RootCauseCategory.MissingNuGetApi;
        }

        // 4. CS1061 (member not found) → likely missing type info or hallucinated API
        if (residualError.ErrorCode == "CS1061")
        {
            if (providedApiSurface != null && ContainsUnprovidedSymbol(llmResponse, providedApiSurface))
                return RootCauseCategory.HallucinatedApi;

            return RootCauseCategory.MissingTypeInfo;
        }

        // 5. Different error code entirely → regression, likely prompt ambiguity or wrong TFM
        if (residualError.ErrorCode != originalError.ErrorCode)
        {
            // Check if it's a TFM-related error (e.g., API not available in the targeted framework)
            if (residualError.Message.Contains("not available", StringComparison.OrdinalIgnoreCase) ||
                residualError.Message.Contains("framework", StringComparison.OrdinalIgnoreCase))
            {
                return RootCauseCategory.WrongTargetFramework;
            }

            return RootCauseCategory.PromptAmbiguity;
        }

        return RootCauseCategory.Unknown;
    }

    /// <summary>
    /// Checks whether the LLM response contains symbols not in the provided API surface.
    /// This is a heuristic — it looks for method-call patterns and member-access patterns.
    /// </summary>
    private bool ContainsUnprovidedSymbol(string response, HashSet<string> providedSymbols)
    {
        // Extract method-like identifiers from the LLM response
        var methodPattern = new System.Text.RegularExpressions.Regex(@"\.(\w+)\s*\(");
        foreach (System.Text.RegularExpressions.Match match in methodPattern.Matches(response))
        {
            var symbol = match.Groups[1].Value;
            // Skip common/well-known methods
            if (IsCommonSymbol(symbol)) continue;

            if (!providedSymbols.Contains(symbol))
                return true;
        }

        // Extract property-access identifiers
        var propertyPattern = new System.Text.RegularExpressions.Regex(@"\.(\w+)\s*[;,\)\]\}]");
        foreach (System.Text.RegularExpressions.Match match in propertyPattern.Matches(response))
        {
            var symbol = match.Groups[1].Value;
            if (IsCommonSymbol(symbol)) continue;

            if (!providedSymbols.Contains(symbol))
                return true;
        }

        return false;
    }

    private static bool IsCommonSymbol(string symbol)
    {
        // Skip very common .NET BCL members that wouldn't be in the provided API surface
        return symbol is "ToString" or "GetType" or "Equals" or "GetHashCode"
            or "Add" or "Remove" or "Contains" or "Count" or "Any" or "First"
            or "FirstOrDefault" or "Where" or "Select" or "ToList" or "ToArray"
            or "OrderBy" or "OrderByDescending" or "GroupBy" or "Append"
            or "Length" or "Trim" or "Split" or "Replace" or "StartsWith" or "EndsWith"
            or "TryGetValue" or "ContainsKey" or "Keys" or "Values"
            or "WriteLine" or "ReadLine" or "Write" or "Format"
            or "ConfigureAwait" or "Wait" or "Result" or "Task"
            or "Dispose" or "Close" or "Flush"
            or "GetAwaiter" or "GetResult";
    }

    /// <summary>
    /// Extracts symbols referenced in an LLM response for negative-example mining.
    /// Returns symbols that look like method or property accesses.
    /// </summary>
    public HashSet<string> ExtractReferencedSymbols(string llmResponse)
    {
        var symbols = new HashSet<string>();

        var pattern = new System.Text.RegularExpressions.Regex(@"\.(\w+)\s*[\(\;\,\)\]\}]");
        foreach (System.Text.RegularExpressions.Match match in pattern.Matches(llmResponse))
        {
            var symbol = match.Groups[1].Value;
            if (!IsCommonSymbol(symbol))
                symbols.Add(symbol);
        }

        return symbols;
    }
}

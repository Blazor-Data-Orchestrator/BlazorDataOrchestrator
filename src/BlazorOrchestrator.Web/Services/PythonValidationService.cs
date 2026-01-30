namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for validating Python code syntax.
/// Note: Full Python compilation validation requires a Python runtime.
/// This service provides basic structural checks.
/// </summary>
public class PythonValidationService
{
    private readonly ILogger<PythonValidationService> _logger;

    public PythonValidationService(ILogger<PythonValidationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates Python code for basic syntax issues.
    /// </summary>
    /// <param name="code">The Python code to validate.</param>
    /// <returns>Validation result with any errors found.</returns>
    public CompilationResult Validate(string code)
    {
        var result = new CompilationResult { Success = true };

        try
        {
            var lines = code.Split('\n');
            var indentStack = new Stack<int>();
            indentStack.Push(0);
            var inMultiLineString = false;
            var stringChar = ' ';

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineNumber = i + 1;

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Track multi-line strings
                var tripleDoubleQuote = line.Contains("\"\"\"");
                var tripleSingleQuote = line.Contains("'''");

                if (tripleDoubleQuote || tripleSingleQuote)
                {
                    var quoteType = tripleDoubleQuote ? '"' : '\'';
                    var count = CountOccurrences(line, tripleDoubleQuote ? "\"\"\"" : "'''");

                    if (inMultiLineString && quoteType == stringChar)
                    {
                        inMultiLineString = count % 2 == 0; // Even count means still in string
                    }
                    else if (!inMultiLineString && count % 2 == 1)
                    {
                        inMultiLineString = true;
                        stringChar = quoteType;
                    }
                }

                // Skip lines inside multi-line strings
                if (inMultiLineString)
                    continue;

                var trimmedLine = line.TrimEnd();

                // Skip comment-only lines
                if (trimmedLine.TrimStart().StartsWith('#'))
                    continue;

                // Check for function definitions
                if (trimmedLine.TrimStart().StartsWith("def "))
                {
                    if (!trimmedLine.TrimEnd().EndsWith(":"))
                    {
                        result.Success = false;
                        result.Errors.Add(new CompilationError
                        {
                            Message = "Function definition should end with ':'",
                            Line = lineNumber,
                            Column = 1,
                            Severity = "Error"
                        });
                    }

                    // Check for proper function syntax
                    if (!trimmedLine.Contains("(") || !trimmedLine.Contains(")"))
                    {
                        result.Success = false;
                        result.Errors.Add(new CompilationError
                        {
                            Message = "Function definition requires parentheses",
                            Line = lineNumber,
                            Column = 1,
                            Severity = "Error"
                        });
                    }
                }

                // Check for class definitions
                if (trimmedLine.TrimStart().StartsWith("class "))
                {
                    if (!trimmedLine.TrimEnd().EndsWith(":"))
                    {
                        result.Success = false;
                        result.Errors.Add(new CompilationError
                        {
                            Message = "Class definition should end with ':'",
                            Line = lineNumber,
                            Column = 1,
                            Severity = "Error"
                        });
                    }
                }

                // Check for if/elif/else statements
                var stripped = trimmedLine.TrimStart();
                if ((stripped.StartsWith("if ") || stripped.StartsWith("elif ")) && 
                    !stripped.EndsWith(":") && 
                    !stripped.Contains(" else ") &&
                    !stripped.Contains(" if "))  // Not a ternary
                {
                    result.Success = false;
                    result.Errors.Add(new CompilationError
                    {
                        Message = "If/elif statement should end with ':'",
                        Line = lineNumber,
                        Column = 1,
                        Severity = "Error"
                    });
                }

                if (stripped.StartsWith("else") && stripped.Trim() == "else" || 
                    (stripped.StartsWith("else:") && stripped.Trim() != "else:"))
                {
                    if (!stripped.TrimEnd().EndsWith(":"))
                    {
                        result.Success = false;
                        result.Errors.Add(new CompilationError
                        {
                            Message = "Else statement should end with ':'",
                            Line = lineNumber,
                            Column = 1,
                            Severity = "Error"
                        });
                    }
                }

                // Check for loop statements
                if ((stripped.StartsWith("for ") || stripped.StartsWith("while ")) && 
                    !stripped.EndsWith(":"))
                {
                    result.Success = false;
                    result.Errors.Add(new CompilationError
                    {
                        Message = "Loop statement should end with ':'",
                        Line = lineNumber,
                        Column = 1,
                        Severity = "Error"
                    });
                }

                // Check for try/except/finally
                if ((stripped == "try" || stripped.StartsWith("try:") ||
                     stripped.StartsWith("except") || stripped.StartsWith("finally")) && 
                    !stripped.EndsWith(":"))
                {
                    result.Success = false;
                    result.Errors.Add(new CompilationError
                    {
                        Message = "Try/except/finally statement should end with ':'",
                        Line = lineNumber,
                        Column = 1,
                        Severity = "Error"
                    });
                }

                // Check for with statement
                if (stripped.StartsWith("with ") && !stripped.EndsWith(":"))
                {
                    result.Success = false;
                    result.Errors.Add(new CompilationError
                    {
                        Message = "With statement should end with ':'",
                        Line = lineNumber,
                        Column = 1,
                        Severity = "Error"
                    });
                }

                // Check for unclosed parentheses, brackets, braces (basic check)
                var openParens = trimmedLine.Count(c => c == '(') - trimmedLine.Count(c => c == ')');
                var openBrackets = trimmedLine.Count(c => c == '[') - trimmedLine.Count(c => c == ']');
                var openBraces = trimmedLine.Count(c => c == '{') - trimmedLine.Count(c => c == '}');

                // Only report if significantly unbalanced (allow for multi-line statements)
                if (openParens > 2)
                {
                    result.Errors.Add(new CompilationError
                    {
                        Message = "Potentially unclosed parentheses",
                        Line = lineNumber,
                        Column = 1,
                        Severity = "Warning"
                    });
                }

                if (openBrackets > 2)
                {
                    result.Errors.Add(new CompilationError
                    {
                        Message = "Potentially unclosed brackets",
                        Line = lineNumber,
                        Column = 1,
                        Severity = "Warning"
                    });
                }

                // Check for mixing tabs and spaces (common Python issue)
                if (line.StartsWith(" ") && line.Contains("\t"))
                {
                    result.Errors.Add(new CompilationError
                    {
                        Message = "Mixing tabs and spaces in indentation",
                        Line = lineNumber,
                        Column = 1,
                        Severity = "Warning"
                    });
                }
            }

            // Check for unclosed multi-line string at end of file
            if (inMultiLineString)
            {
                result.Success = false;
                result.Errors.Add(new CompilationError
                {
                    Message = "Unclosed multi-line string",
                    Line = lines.Length,
                    Column = 1,
                    Severity = "Error"
                });
            }

            _logger.LogInformation("Python validation {Status} with {ErrorCount} issues",
                result.Success ? "passed" : "failed",
                result.Errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Python validation failed with exception");
            result.Success = false;
            result.Errors.Add(new CompilationError
            {
                Message = $"Validation exception: {ex.Message}",
                Line = 1,
                Column = 1,
                Severity = "Error"
            });
        }

        return result;
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}

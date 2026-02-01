using System.Reflection;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Provides access to embedded instruction files for AI code assistance.
/// </summary>
public class EmbeddedInstructionsProvider
{
    private readonly Assembly _assembly;
    private string? _cachedCSharpInstructions;
    private string? _cachedPythonInstructions;

    public EmbeddedInstructionsProvider()
    {
        _assembly = typeof(EmbeddedInstructionsProvider).Assembly;
    }

    /// <summary>
    /// Gets the C# code generation instructions.
    /// </summary>
    public string GetCSharpInstructions()
    {
        if (_cachedCSharpInstructions != null)
            return _cachedCSharpInstructions;

        _cachedCSharpInstructions = LoadEmbeddedResource("csharp.instructions.md");
        return _cachedCSharpInstructions;
    }

    /// <summary>
    /// Gets the Python code generation instructions.
    /// </summary>
    public string GetPythonInstructions()
    {
        if (_cachedPythonInstructions != null)
            return _cachedPythonInstructions;

        _cachedPythonInstructions = LoadEmbeddedResource("python.instructions.md");
        return _cachedPythonInstructions;
    }

    /// <summary>
    /// Gets instructions for the specified language.
    /// </summary>
    public string GetInstructionsForLanguage(string language)
    {
        return language?.ToLowerInvariant() switch
        {
            "python" => GetPythonInstructions(),
            "csharp" or "c#" => GetCSharpInstructions(),
            _ => GetCSharpInstructions()
        };
    }

    private string LoadEmbeddedResource(string resourceName)
    {
        var fullResourceName = _assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

        if (fullResourceName == null)
            return string.Empty;

        using var stream = _assembly.GetManifestResourceStream(fullResourceName);
        if (stream == null)
            return string.Empty;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

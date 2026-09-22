namespace BlazorOrchestrator.Testing;

public static class JobScenarioCatalog
{
    public static JobScenario Get(string id)
    {
        if (id.Length != 6 || id[3] != '-' || !int.TryParse(id.AsSpan(4), out var ordinal) || ordinal is < 1 or > 8)
        {
            throw new ArgumentException($"Unknown scenario ID '{id}'.", nameof(id));
        }

        var surface = id[0] == 'W' ? JobSurface.Web : id[0] == 'V' ? JobSurface.VisualStudio : throw new ArgumentException($"Unknown scenario ID '{id}'.", nameof(id));
        var language = id.Substring(1, 2) == "CS" ? JobLanguage.CSharp : id.Substring(1, 2) == "PY" ? JobLanguage.Python : throw new ArgumentException($"Unknown scenario ID '{id}'.", nameof(id));
        return new JobScenario(id, surface, language, ordinal >= 5, ordinal is 3 or 4 or 7 or 8, ordinal % 2 == 0);
    }
}
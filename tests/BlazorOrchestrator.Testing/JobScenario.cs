namespace BlazorOrchestrator.Testing;

public enum JobSurface
{
    Web,
    VisualStudio
}

public enum JobLanguage
{
    CSharp,
    Python
}

public sealed record JobScenario(
    string Id,
    JobSurface Surface,
    JobLanguage Language,
    bool IsUpdate,
    bool UsesAi,
    bool HasDependency)
{
    public string ExpectedToken => CanonicalJobSources.GetToken(Language, IsUpdate, HasDependency);
    public string? ObsoleteToken => IsUpdate ? CanonicalJobSources.GetToken(Language, false, HasDependency) : null;
}
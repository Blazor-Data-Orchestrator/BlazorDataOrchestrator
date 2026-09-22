namespace BlazorOrchestrator.Testing;

public sealed class ScriptedAiService
{
    private readonly List<AiRequest> _requests = new();

    public IReadOnlyList<AiRequest> Requests => _requests;

    public string Generate(JobScenario scenario, string? currentCode)
    {
        _requests.Add(new AiRequest(scenario.Id, scenario.Language, currentCode));
        return CanonicalJobSources.Create(scenario.Language, scenario.IsUpdate, scenario.HasDependency, true);
    }
}

public sealed record AiRequest(string ScenarioId, JobLanguage Language, string? CurrentCode);
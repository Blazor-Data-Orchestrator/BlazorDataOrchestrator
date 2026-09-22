namespace BlazorOrchestrator.Testing;

public static class CanonicalJobSources
{
    public const string CSharpDependencyId = "Humanizer.Core";
    public const string CSharpDependencyVersion = "2.14.1";
    public const string PythonDependency = "packaging==25.0";

    public static string GetToken(JobLanguage language, bool isUpdate, bool hasDependency) =>
        (language, isUpdate, hasDependency) switch
        {
            (JobLanguage.CSharp, false, false) => "CS_CREATE_SIMPLE",
            (JobLanguage.CSharp, false, true) => "CS_CREATE_DEP",
            (JobLanguage.CSharp, true, false) => "CS_UPDATE_SIMPLE",
            (JobLanguage.CSharp, true, true) => "CS_UPDATE_DEP",
            (JobLanguage.Python, false, false) => "PY_CREATE_SIMPLE",
            (JobLanguage.Python, false, true) => "PY_CREATE_DEP",
            (JobLanguage.Python, true, false) => "PY_UPDATE_SIMPLE",
            (JobLanguage.Python, true, true) => "PY_UPDATE_DEP",
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

    public static string Create(JobLanguage language, bool isUpdate, bool hasDependency, bool aiAuthored)
    {
        var token = GetToken(language, isUpdate, hasDependency);
        var marker = aiAuthored ? "AI_AUTHORED" : "MANUAL_AUTHORED";
        return language == JobLanguage.CSharp
            ? CreateCSharp(token, marker, hasDependency)
            : CreatePython(token, marker, hasDependency);
    }

    private static string CreateCSharp(string token, string marker, bool hasDependency)
    {
        var dependencyHeader = hasDependency ? $"// NUGET: {CSharpDependencyId}, {CSharpDependencyVersion}\n" : string.Empty;
        var dependencyUsing = hasDependency ? "using Humanizer;\n" : string.Empty;
        var expression = hasDependency ? $"\"{token}\".Humanize(LetterCasing.AllCaps)" : $"\"{token}\"";
        return $$"""
            {{dependencyHeader}}{{dependencyUsing}}using System.Collections.Generic;
            using System.Threading.Tasks;

            public class BlazorDataOrchestratorJob
            {
                public static Task<List<string>> ExecuteJob(
                    string appSettings,
                    int jobAgentId,
                    int jobId,
                    int jobInstanceId,
                    int jobScheduleId,
                    string webAPIParameter)
                {
                    const string authoringMarker = "{{marker}}";
                    _ = authoringMarker;
                    return Task.FromResult(new List<string> { {{expression}} });
                }
            }
            """;
    }

    private static string CreatePython(string token, string marker, bool hasDependency)
    {
        var dependencyHeader = hasDependency ? $"# ADD TO REQUIREMENTS.txt: {PythonDependency}\nfrom packaging.version import Version\n" : string.Empty;
        var dependencyUse = hasDependency ? "\n    assert Version(\"1.0\").major == 1" : string.Empty;
        return $$"""
            {{dependencyHeader}}def execute_job(app_settings: str, job_agent_id: int, job_id: int, job_instance_id: int, job_schedule_id: int) -> list[str]:
                authoring_marker = "{{marker}}"{{dependencyUse}}
                return ["{{token}}", authoring_marker]
            """;
    }
}
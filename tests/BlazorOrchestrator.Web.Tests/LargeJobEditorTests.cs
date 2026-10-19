using System.Text.RegularExpressions;
using BlazorOrchestrator.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorOrchestrator.Web.Tests;

/// <summary>
/// Regressions for large jobs (e.g. FYExchange) that worked in the job designer and on the Agent but could not
/// be switched between files or compiled in the web editor.
/// </summary>
public class LargeJobEditorTests
{
    [Fact(DisplayName = "Web Save & Compile references the full .NET runtime, like the Agent")]
    [Trait("Category", "Contract")]
    public async Task CompileWithDependencies_SystemDataAndDynamic_Compiles()
    {
        // SqlBulkCopyColumnMappingCollection derives from CollectionBase (System.Collections.NonGeneric) and
        // DataTable implements IXmlSerializable (System.Private.Xml); 'dynamic' needs Microsoft.CSharp.
        const string code = """
            using System.Collections.Generic;
            using System.Data;
            using System.Threading.Tasks;
            using Microsoft.Data.SqlClient;

            public class BlazorDataOrchestratorJob
            {
                public static async Task<List<string>> ExecuteJob(string appSettings, int jobAgentId, int jobId,
                    int jobInstanceId, int jobScheduleId, string webAPIParameter)
                {
                    var table = new DataTable();
                    table.Columns.Add("Id", typeof(int));
                    using var bulk = new SqlBulkCopy("Server=.;Database=x;Trusted_Connection=True");
                    bulk.ColumnMappings.Add("Id", "Id");
                    dynamic value = table.Rows.Count;
                    await Task.CompletedTask;
                    return new List<string> { value.ToString() };
                }
            }
            """;

        var compiler = new CSharpCompilationService(NullLogger<CSharpCompilationService>.Instance);
        var result = await compiler.CompileWithDependenciesAsync(code);

        Assert.True(result.Success, string.Join(Environment.NewLine,
            result.Errors.Where(e => e.Severity == "Error").Select(e => $"L{e.Line}: {e.Message}")));
    }

    [Theory(DisplayName = "Editors raise the 32 KB SignalR limit so large job files survive JS interop")]
    [Trait("Category", "Contract")]
    [InlineData("BlazorOrchestrator.Web")]
    [InlineData("BlazorDataOrchestrator.JobCreatorTemplate")]
    public void Program_RaisesHubMaximumReceiveMessageSize(string project)
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", project, "Program.cs"));

        var match = Regex.Match(program, @"MaximumReceiveMessageSize\s*=\s*(\d+)\s*\*\s*(\d+)");
        Assert.True(match.Success, $"{project}/Program.cs must set HubOptions.MaximumReceiveMessageSize.");
        Assert.True(long.Parse(match.Groups[1].Value) * long.Parse(match.Groups[2].Value) >= 1024 * 1024);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BlazorDataOrchestrator.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

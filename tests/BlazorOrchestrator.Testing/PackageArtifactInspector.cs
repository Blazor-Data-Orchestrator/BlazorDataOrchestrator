using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace BlazorOrchestrator.Testing;

public sealed class PackageArtifactInspector : IDisposable
{
    private readonly ZipArchive _archive;

    public PackageArtifactInspector(Stream packageStream)
    {
        _archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
    }

    public IReadOnlyCollection<string> Entries => _archive.Entries.Select(entry => entry.FullName).ToArray();

    public string ReadRequired(string suffix)
    {
        var matches = _archive.Entries
            .Where(entry => entry.FullName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException($"Expected exactly one package entry ending with '{suffix}', found {matches.Length}.");
        }

        using var reader = new StreamReader(matches[0].Open());
        return reader.ReadToEnd();
    }

    public void AssertScenario(JobScenario scenario)
    {
        var sourceSuffix = scenario.Language == JobLanguage.CSharp ? "CodeCSharp/main.cs" : "CodePython/main.py";
        var otherSuffix = scenario.Language == JobLanguage.CSharp ? "CodePython/main.py" : "CodeCSharp/main.cs";
        var source = ReadRequired(sourceSuffix);
        Require(source.Contains(scenario.ExpectedToken, StringComparison.Ordinal), $"Source does not contain {scenario.ExpectedToken}.");
        if (scenario.ObsoleteToken is not null)
        {
            Require(!source.Contains(scenario.ObsoleteToken, StringComparison.Ordinal), $"Source still contains {scenario.ObsoleteToken}.");
        }

        Require(!_archive.Entries.Any(entry => entry.FullName.EndsWith(otherSuffix, StringComparison.OrdinalIgnoreCase)), "Package contains both language entry points.");
        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json", "appsettings.Staging.json", "appsettings.Production.json" })
        {
            _ = ReadRequired(fileName);
        }

        var configuration = JsonDocument.Parse(ReadRequired("configuration.json"));
        var expectedLanguage = scenario.Language == JobLanguage.CSharp ? "csharp" : "python";
        Require(configuration.RootElement.GetProperty("SelectedLanguage").GetString() == expectedLanguage, "SelectedLanguage is incorrect.");

        if (scenario.Language == JobLanguage.CSharp)
        {
            var nuspec = XDocument.Parse(ReadRequired(".nuspec"));
            XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
            var dependency = nuspec.Descendants(ns + "dependency")
                .SingleOrDefault(element => string.Equals((string?)element.Attribute("id"), CanonicalJobSources.CSharpDependencyId, StringComparison.OrdinalIgnoreCase));
            Require(scenario.HasDependency == (dependency is not null), "C# dependency metadata does not match the scenario.");
            if (dependency is not null)
            {
                Require((string?)dependency.Attribute("version") == CanonicalJobSources.CSharpDependencyVersion, "C# dependency version is not pinned correctly.");
            }
        }
        else if (scenario.HasDependency)
        {
            Require(ReadRequired("CodePython/requirements.txt").Split('\n', StringSplitOptions.TrimEntries).Contains(CanonicalJobSources.PythonDependency), "Python requirement is missing.");
        }
    }

    public void Dispose() => _archive.Dispose();

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
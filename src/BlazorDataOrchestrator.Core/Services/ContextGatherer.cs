using BlazorDataOrchestrator.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Gathers contextual information for a build error using Roslyn analysis.
/// Extracts related type summaries, project metadata, and source file content
/// to provide the LLM with sufficient context for generating correct fixes.
/// </summary>
public class ContextGatherer
{
    /// <summary>
    /// Gathers full context for a build error from the source file and project metadata.
    /// Uses Roslyn to parse the source and extract related type information.
    /// </summary>
    /// <param name="error">The build error to gather context for.</param>
    /// <param name="solutionRootPath">Root path of the solution.</param>
    /// <param name="negativeExamples">Known negative examples for this error code.</param>
    /// <returns>A <see cref="BuildErrorContext"/> populated with all available context.</returns>
    public BuildErrorContext GatherContext(
        BuildError error,
        string solutionRootPath,
        IReadOnlyList<NegativeExample>? negativeExamples = null)
    {
        // Read the source file
        var sourceContent = File.Exists(error.FilePath) 
            ? File.ReadAllText(error.FilePath) 
            : "";

        // Extract project metadata
        var (projectName, targetFramework, aspireVersion, packages) = 
            ExtractProjectMetadata(error.FilePath, solutionRootPath);

        // Use Roslyn to extract type summaries from the source file
        var relatedTypes = ExtractRelatedTypes(sourceContent, error);

        return new BuildErrorContext
        {
            Error = error,
            SourceFileContent = sourceContent,
            RelatedTypes = relatedTypes,
            Packages = packages,
            ProjectName = projectName.Length > 0 ? projectName : error.Project,
            TargetFramework = targetFramework.Length > 0 ? targetFramework : error.TargetFramework,
            AspireVersion = aspireVersion,
            NegativeExamples = negativeExamples?.ToList() ?? []
        };
    }

    /// <summary>
    /// Expands the context by including additional files referenced from the error location.
    /// Called on retry attempts to provide the LLM with broader context.
    /// </summary>
    public BuildErrorContext ExpandContext(
        BuildErrorContext existing,
        string solutionRootPath,
        int expansionLevel = 1)
    {
        var expandedTypes = new List<TypeSummary>(existing.RelatedTypes);

        // Level 1: Look for usages of the error symbol in sibling files
        if (expansionLevel >= 1)
        {
            var projectDir = FindProjectDirectory(existing.Error.FilePath, solutionRootPath);
            if (projectDir != null)
            {
                var siblingTypes = ScanSiblingFiles(projectDir, existing.Error, existing.SourceFileContent);
                foreach (var t in siblingTypes)
                {
                    if (!expandedTypes.Any(et => et.FullName == t.FullName))
                        expandedTypes.Add(t);
                }
            }
        }

        // Level 2: Also scan referenced project directories
        if (expansionLevel >= 2)
        {
            var referencedProjects = FindProjectReferences(existing.Error.FilePath, solutionRootPath);
            foreach (var refProjectDir in referencedProjects)
            {
                var refTypes = ScanSiblingFiles(refProjectDir, existing.Error, existing.SourceFileContent);
                foreach (var t in refTypes)
                {
                    if (!expandedTypes.Any(et => et.FullName == t.FullName))
                        expandedTypes.Add(t);
                }
            }
        }

        return new BuildErrorContext
        {
            Error = existing.Error,
            SourceFileContent = existing.SourceFileContent,
            RelatedTypes = expandedTypes,
            Packages = existing.Packages,
            ProjectName = existing.ProjectName,
            TargetFramework = existing.TargetFramework,
            AspireVersion = existing.AspireVersion,
            NegativeExamples = existing.NegativeExamples
        };
    }

    /// <summary>
    /// Uses Roslyn to parse the source file and extract type summaries of types
    /// referenced near the error location.
    /// </summary>
    private List<TypeSummary> ExtractRelatedTypes(string sourceContent, BuildError error)
    {
        var summaries = new List<TypeSummary>();

        if (string.IsNullOrWhiteSpace(sourceContent))
            return summaries;

        try
        {
            var tree = CSharpSyntaxTree.ParseText(sourceContent);
            var root = tree.GetCompilationUnitRoot();

            // Find the node at the error location
            var errorPosition = tree.GetText().Lines[Math.Max(0, error.Line - 1)].Start + Math.Max(0, error.Column - 1);
            var errorNode = root.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(
                Math.Min(errorPosition, sourceContent.Length - 1), 1));

            // Walk up to find the enclosing type declaration
            var enclosingType = errorNode?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (enclosingType != null)
            {
                summaries.Add(ExtractTypeSummary(enclosingType));
            }

            // Also extract all type declarations in the file
            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var summary = ExtractTypeSummary(typeDecl);
                if (!summaries.Any(s => s.FullName == summary.FullName))
                    summaries.Add(summary);
            }

            // Extract types referenced by identifier near the error (best-effort)
            var enclosingMethod = errorNode?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (enclosingMethod != null)
            {
                var identifiers = enclosingMethod.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Select(id => id.Identifier.Text)
                    .Distinct()
                    .ToList();

                // Check if any of these identifiers match type declarations in the file
                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (identifiers.Contains(typeDecl.Identifier.Text))
                    {
                        var summary = ExtractTypeSummary(typeDecl);
                        if (!summaries.Any(s => s.FullName == summary.FullName))
                            summaries.Add(summary);
                    }
                }
            }
        }
        catch
        {
            // Fall back to regex-based extraction if Roslyn parsing fails
            summaries.AddRange(ExtractTypesWithRegex(sourceContent));
        }

        return summaries;
    }

    /// <summary>
    /// Extracts a public member summary from a type declaration syntax node.
    /// </summary>
    private TypeSummary ExtractTypeSummary(TypeDeclarationSyntax typeDecl)
    {
        var sb = new StringBuilder();
        var namespaceName = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";
        var fullName = string.IsNullOrEmpty(namespaceName)
            ? typeDecl.Identifier.Text
            : $"{namespaceName}.{typeDecl.Identifier.Text}";

        // Type declaration line
        sb.AppendLine($"// {typeDecl.Keyword.Text} {typeDecl.Identifier.Text}");

        // Public members
        foreach (var member in typeDecl.Members)
        {
            if (member is MethodDeclarationSyntax method)
            {
                var modifiers = method.Modifiers.ToString();
                if (modifiers.Contains("public") || modifiers.Contains("internal"))
                {
                    sb.AppendLine($"  {method.ReturnType} {method.Identifier}({FormatParameters(method.ParameterList)});");
                }
            }
            else if (member is PropertyDeclarationSyntax prop)
            {
                var modifiers = prop.Modifiers.ToString();
                if (modifiers.Contains("public") || modifiers.Contains("internal"))
                {
                    sb.AppendLine($"  {prop.Type} {prop.Identifier} {{ get; set; }}");
                }
            }
            else if (member is FieldDeclarationSyntax field)
            {
                var modifiers = field.Modifiers.ToString();
                if (modifiers.Contains("public"))
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        sb.AppendLine($"  {field.Declaration.Type} {variable.Identifier};");
                    }
                }
            }
        }

        return new TypeSummary(fullName, sb.ToString());
    }

    private string FormatParameters(ParameterListSyntax paramList)
    {
        return string.Join(", ", paramList.Parameters.Select(p => $"{p.Type} {p.Identifier}"));
    }

    /// <summary>
    /// Fallback: Extract type information using regex when Roslyn parsing fails.
    /// </summary>
    private List<TypeSummary> ExtractTypesWithRegex(string sourceContent)
    {
        var summaries = new List<TypeSummary>();
        var typePattern = new Regex(@"(?:public|internal)\s+(?:sealed\s+|abstract\s+|static\s+)*(?:class|record|struct|interface)\s+(\w+)",
            RegexOptions.Multiline);

        foreach (Match match in typePattern.Matches(sourceContent))
        {
            summaries.Add(new TypeSummary(match.Groups[1].Value, $"// {match.Value} (regex-extracted, limited info)"));
        }

        return summaries;
    }

    /// <summary>
    /// Scans sibling .cs files in the project directory for types referenced by the error.
    /// </summary>
    private List<TypeSummary> ScanSiblingFiles(string projectDir, BuildError error, string errorFileContent)
    {
        var summaries = new List<TypeSummary>();

        try
        {
            // Extract identifiers from the error message to know what to look for
            var identifiers = ExtractIdentifiersFromErrorMessage(error.Message);

            foreach (var csFile in Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories))
            {
                if (csFile == error.FilePath) continue;

                try
                {
                    var content = File.ReadAllText(csFile);

                    // Only parse if the file likely contains a referenced identifier
                    if (!identifiers.Any(id => content.Contains(id)))
                        continue;

                    var tree = CSharpSyntaxTree.ParseText(content);
                    var root = tree.GetCompilationUnitRoot();

                    foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        if (identifiers.Contains(typeDecl.Identifier.Text))
                        {
                            summaries.Add(ExtractTypeSummary(typeDecl));
                        }
                    }
                }
                catch
                {
                    // Skip files that can't be parsed
                }
            }
        }
        catch
        {
            // Directory not accessible
        }

        return summaries;
    }

    /// <summary>
    /// Extracts plausible type/member identifiers from a build error message.
    /// </summary>
    private HashSet<string> ExtractIdentifiersFromErrorMessage(string message)
    {
        var identifiers = new HashSet<string>();

        // Match patterns like 'TypeName' or 'TypeName.MemberName' in error messages
        var pattern = new Regex(@"'(\w+(?:\.\w+)*)'");
        foreach (Match m in pattern.Matches(message))
        {
            var parts = m.Groups[1].Value.Split('.');
            foreach (var part in parts)
            {
                if (part.Length > 1) // Skip single characters
                    identifiers.Add(part);
            }
        }

        // Also match CS1061-style: 'Type' does not contain a definition for 'Member'
        var memberPattern = new Regex(@"does not contain a definition for '(\w+)'");
        foreach (Match m in memberPattern.Matches(message))
        {
            identifiers.Add(m.Groups[1].Value);
        }

        return identifiers;
    }

    /// <summary>
    /// Extracts project metadata from the .csproj file nearest to the error file.
    /// </summary>
    private (string ProjectName, string TargetFramework, string AspireVersion, List<PackageInfo> Packages) 
        ExtractProjectMetadata(string filePath, string solutionRoot)
    {
        var projectName = "";
        var targetFramework = "";
        var aspireVersion = "";
        var packages = new List<PackageInfo>();

        try
        {
            var csprojPath = FindNearestCsproj(filePath, solutionRoot);
            if (csprojPath == null) return (projectName, targetFramework, aspireVersion, packages);

            projectName = Path.GetFileNameWithoutExtension(csprojPath);
            var doc = XDocument.Load(csprojPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            targetFramework = doc.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value ?? "";

            foreach (var pkgRef in doc.Descendants(ns + "PackageReference"))
            {
                var id = pkgRef.Attribute("Include")?.Value ?? "";
                var version = pkgRef.Attribute("Version")?.Value ?? "";
                if (!string.IsNullOrEmpty(id))
                {
                    packages.Add(new PackageInfo(id, version));
                    if (id.StartsWith("Aspire.", StringComparison.OrdinalIgnoreCase) && aspireVersion == "")
                        aspireVersion = version;
                }
            }
        }
        catch
        {
            // Project file not readable
        }

        return (projectName, targetFramework, aspireVersion, packages);
    }

    private string? FindNearestCsproj(string filePath, string solutionRoot)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (dir != null && dir.StartsWith(solutionRoot, StringComparison.OrdinalIgnoreCase))
        {
            var csproj = Directory.GetFiles(dir, "*.csproj").FirstOrDefault();
            if (csproj != null) return csproj;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private string? FindProjectDirectory(string filePath, string solutionRoot)
    {
        var csproj = FindNearestCsproj(filePath, solutionRoot);
        return csproj != null ? Path.GetDirectoryName(csproj) : null;
    }

    /// <summary>
    /// Finds directories of referenced projects from the .csproj.
    /// </summary>
    private List<string> FindProjectReferences(string filePath, string solutionRoot)
    {
        var dirs = new List<string>();

        try
        {
            var csproj = FindNearestCsproj(filePath, solutionRoot);
            if (csproj == null) return dirs;

            var doc = XDocument.Load(csproj);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var csprojDir = Path.GetDirectoryName(csproj)!;

            foreach (var projRef in doc.Descendants(ns + "ProjectReference"))
            {
                var includePath = projRef.Attribute("Include")?.Value;
                if (includePath != null)
                {
                    var fullPath = Path.GetFullPath(Path.Combine(csprojDir, includePath));
                    var refDir = Path.GetDirectoryName(fullPath);
                    if (refDir != null && Directory.Exists(refDir))
                        dirs.Add(refDir);
                }
            }
        }
        catch
        {
            // Ignore errors in project reference resolution
        }

        return dirs;
    }
}

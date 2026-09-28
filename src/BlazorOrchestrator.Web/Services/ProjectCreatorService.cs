using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using BlazorDataOrchestrator.Core.Models;
using BlazorDataOrchestrator.Core.Services;

namespace BlazorOrchestrator.Web.Services;

public class ProjectCreatorService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ProjectCreatorService> _logger;

    public ProjectCreatorService(IWebHostEnvironment environment, ILogger<ProjectCreatorService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<ProjectCreationResult> CreateProjectAsync(string projectName)
    {
        try
        {
            // Validate project name
            if (string.IsNullOrWhiteSpace(projectName))
            {
                return new ProjectCreationResult { Success = false, ErrorMessage = "Project name is required." };
            }

            if (projectName.Length > 20)
            {
                return new ProjectCreationResult { Success = false, ErrorMessage = "Project name must be 20 characters or less." };
            }

            if (projectName.Contains(' '))
            {
                return new ProjectCreationResult { Success = false, ErrorMessage = "Project name cannot contain spaces." };
            }

            // Get the path to the zip template
            var templateZipPath = Path.Combine(_environment.ContentRootPath, "JobTemplate", "BlazorDataOrchestrator.JobCreatorTemplate.zip");
            
            if (!File.Exists(templateZipPath))
            {
                _logger.LogError("Template zip file not found at: {Path}", templateZipPath);
                return new ProjectCreationResult { Success = false, ErrorMessage = "Template file not found." };
            }

            // Determine the output directory (sibling to current project)
            var currentProjectDirectory = _environment.ContentRootPath;
            var parentDirectory = Directory.GetParent(currentProjectDirectory)?.FullName;
            
            if (string.IsNullOrEmpty(parentDirectory))
            {
                return new ProjectCreationResult { Success = false, ErrorMessage = "Could not determine parent directory." };
            }

            var outputDirectory = Path.Combine(parentDirectory, projectName);

            // Check if the output directory already exists
            if (Directory.Exists(outputDirectory))
            {
                return new ProjectCreationResult { Success = false, ErrorMessage = $"A project with the name '{projectName}' already exists." };
            }

            // Create the output directory
            Directory.CreateDirectory(outputDirectory);

            _logger.LogInformation("Creating project '{ProjectName}' at: {OutputPath}", projectName, outputDirectory);

            // Extract the zip file
            ExtractTemplateNormalized(templateZipPath, outputDirectory);

            // Replace all instances of "JobCreatorTemplate" with the new project name
            await ReplaceInFilesAndNamesAsync(outputDirectory, "JobCreatorTemplate", projectName);

            _logger.LogInformation("Project '{ProjectName}' created successfully", projectName);

            return new ProjectCreationResult 
            { 
                Success = true, 
                OutputPath = outputDirectory 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create project '{ProjectName}'", projectName);
            return new ProjectCreationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task ReplaceInFilesAndNamesAsync(string directory, string oldValue, string newValue)
    {
        // First, process all files in the directory (replace content)
        var allFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        
        foreach (var filePath in allFiles)
        {
            await ReplaceInFileContentAsync(filePath, oldValue, newValue);
        }

        // Then rename files that contain the old value in their name
        // Process files first
        allFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        foreach (var filePath in allFiles)
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.Contains(oldValue, StringComparison.OrdinalIgnoreCase))
            {
                var newFileName = fileName.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);
                var newFilePath = Path.Combine(Path.GetDirectoryName(filePath)!, newFileName);
                
                if (filePath != newFilePath)
                {
                    File.Move(filePath, newFilePath);
                    _logger.LogDebug("Renamed file: {OldPath} -> {NewPath}", filePath, newFilePath);
                }
            }
        }

        // Rename directories that contain the old value in their name
        // Process from deepest to shallowest to avoid path issues
        var allDirectories = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length)
            .ToList();

        foreach (var dirPath in allDirectories)
        {
            var dirName = Path.GetFileName(dirPath);
            if (dirName.Contains(oldValue, StringComparison.OrdinalIgnoreCase))
            {
                var newDirName = dirName.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);
                var newDirPath = Path.Combine(Path.GetDirectoryName(dirPath)!, newDirName);
                
                if (dirPath != newDirPath && Directory.Exists(dirPath))
                {
                    Directory.Move(dirPath, newDirPath);
                    _logger.LogDebug("Renamed directory: {OldPath} -> {NewPath}", dirPath, newDirPath);
                }
            }
        }
    }

    private async Task ReplaceInFileContentAsync(string filePath, string oldValue, string newValue)
    {
        try
        {
            // Skip binary files based on extension
            var binaryExtensions = new[] { ".dll", ".exe", ".pdb", ".zip", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".woff", ".woff2", ".ttf", ".eot" };
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            if (binaryExtensions.Contains(extension))
            {
                return;
            }

            var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            
            if (content.Contains(oldValue, StringComparison.OrdinalIgnoreCase))
            {
                // Replace with case-sensitive replacement to preserve original casing patterns
                var newContent = content.Replace(oldValue, newValue);
                await File.WriteAllTextAsync(filePath, newContent, Encoding.UTF8);
                _logger.LogDebug("Replaced content in file: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not process file: {FilePath}", filePath);
        }
    }

    public async Task<ProjectCreationResult> CreateProjectWithCodeAsync(
        string projectName,
        Dictionary<string, string> codeFiles,
        int jobId = 0)
    {
        // 1. Use existing method to create the project on disk
        var result = await CreateProjectAsync(projectName);

        if (!result.Success || string.IsNullOrEmpty(result.OutputPath))
        {
            return result;
        }

        // 2. Inject code files into the project's Code/ subdirectory
        try
        {
            InjectCodeFiles(result.OutputPath, codeFiles, jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inject code into project '{ProjectName}'", projectName);
            return new ProjectCreationResult { Success = false, ErrorMessage = ex.Message };
        }

        return result;
    }

    /// <summary>
    /// Writes the job's code files into the extracted project's Code/ folder, routing each file
    /// into the language subfolder (CodeCSharp / CodePython) the template expects.
    /// </summary>
    private void InjectCodeFiles(string outputDirectory, Dictionary<string, string>? codeFiles, int jobId)
    {
        if ((codeFiles == null || codeFiles.Count == 0) && jobId <= 0)
        {
            return;
        }

        var projectDirectory = ResolveProjectDirectory(outputDirectory);

        foreach (var (fileName, fileContent) in codeFiles ?? new Dictionary<string, string>())
        {
            // Guard against path traversal from package entry names
            var safeFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                continue;
            }

            // The package manifest only feeds the .csproj; the designer regenerates it from the project.
            if (safeFileName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetDirectory = Path.Combine(projectDirectory, "Code", GetLanguageSubFolder(safeFileName));
            Directory.CreateDirectory(targetDirectory);

            var filePath = Path.Combine(targetDirectory, safeFileName);
            File.WriteAllText(filePath, fileContent);
            _logger.LogInformation("Injected code file: {FilePath}", filePath);
        }

        if (jobId > 0)
        {
            PointConfigurationAtJob(Path.Combine(projectDirectory, "Code", "configuration.json"), jobId);
        }

        var projectFile = Directory.EnumerateFiles(projectDirectory, "*.csproj").First();
        var added = AddJobPackageReferences(projectFile, CollectJobDependencies(codeFiles));
        foreach (var dependency in added)
        {
            _logger.LogInformation("Added job package reference {Dependency} to {ProjectFile}", dependency, projectFile);
        }
    }

    /// <summary>
    /// Reads the job's NuGet dependencies from the package manifest and dependencies.json; the manifest wins on conflicts.
    /// </summary>
    internal static List<PackageDependency> CollectJobDependencies(IReadOnlyDictionary<string, string>? codeFiles)
    {
        var dependencies = new List<PackageDependency>();
        if (codeFiles == null)
        {
            return dependencies;
        }

        void Add(string? id, string? version)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)
                || dependencies.Any(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            dependencies.Add(new PackageDependency { Id = id.Trim(), Version = version.Trim() });
        }

        foreach (var (name, content) in codeFiles.Where(f => f.Key.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                foreach (var element in XDocument.Parse(content).Descendants().Where(e => e.Name.LocalName == "dependency"))
                {
                    Add(element.Attribute("id")?.Value, element.Attribute("version")?.Value);
                }
            }
            catch (System.Xml.XmlException)
            {
            }
        }

        var dependenciesJson = codeFiles.FirstOrDefault(f => Path.GetFileName(f.Key).Equals("dependencies.json", StringComparison.OrdinalIgnoreCase)).Value;
        if (!string.IsNullOrWhiteSpace(dependenciesJson))
        {
            try
            {
                var config = JsonSerializer.Deserialize<DependenciesConfig>(dependenciesJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                foreach (var dependency in config?.Dependencies ?? new List<PackageDependency>())
                {
                    Add(dependency.Id, dependency.Version);
                }
            }
            catch (JsonException)
            {
            }
        }

        return dependencies;
    }

    /// <summary>
    /// Adds a PackageReference for each job dependency the designer project does not already supply.
    /// </summary>
    internal static List<PackageDependency> AddJobPackageReferences(string projectFile, IEnumerable<PackageDependency> dependencies)
    {
        var document = XDocument.Load(projectFile, LoadOptions.PreserveWhitespace);
        var project = document.Root!;
        var ns = project.Name.Namespace;

        var existing = new HashSet<string>(
            project.Descendants(ns + "PackageReference")
                .Select(r => r.Attribute("Include")?.Value)
                .Where(id => !string.IsNullOrEmpty(id))!,
            StringComparer.OrdinalIgnoreCase);

        // Host packages come from the designer's own references at the platform version;
        // pinning a job's (possibly older) version would fail restore with NU1605.
        var added = dependencies
            .Where(d => !existing.Contains(d.Id))
            .Where(d => !d.Version.Contains('*'))
            .Where(d => !NuGetPackageBuilderService.DefaultDependencies.Any(h => h.Id.Equals(d.Id, StringComparison.OrdinalIgnoreCase)))
            .Where(d => !d.Id.StartsWith("BlazorDataOrchestrator.", StringComparison.OrdinalIgnoreCase))
            .Where(d => !NuGetPackageBuilderService.DesignerHostPackagePrefixes.Any(p =>
                d.Id.StartsWith(p, StringComparison.OrdinalIgnoreCase) || d.Id.Equals(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (added.Count == 0)
        {
            return added;
        }

        var itemGroup = new XElement(ns + "ItemGroup", new XAttribute("Label", "Job dependencies"));
        foreach (var dependency in added)
        {
            itemGroup.Add("\n    ", new XElement(ns + "PackageReference",
                new XAttribute("Include", dependency.Id),
                new XAttribute("Version", dependency.Version)));
        }
        itemGroup.Add("\n  ");

        project.Add("  ", itemGroup, "\n\n");

        using (var writer = System.Xml.XmlWriter.Create(projectFile, new System.Xml.XmlWriterSettings { OmitXmlDeclaration = true, Encoding = new UTF8Encoding(false) }))
        {
            document.Save(writer);
        }

        return added;
    }

    /// <summary>
    /// Makes the designer reuse the platform job instead of the job that last uploaded the package.
    /// </summary>
    private static void PointConfigurationAtJob(string configurationPath, int jobId)
    {
        JsonObject? configuration = null;
        if (File.Exists(configurationPath))
        {
            try { configuration = JsonNode.Parse(File.ReadAllText(configurationPath)) as JsonObject; }
            catch (JsonException) { }
        }

        configuration ??= new JsonObject { ["SelectedLanguage"] = "csharp" };
        configuration["LastJobId"] = jobId;
        configuration["LastJobInstanceId"] = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(configurationPath)!);
        File.WriteAllText(configurationPath, configuration.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ResolveProjectDirectory(string outputDirectory)
    {
        // The template zip nests the project under "BlazorDataOrchestrator.{projectName}" after renaming,
        // so locate it by its .csproj rather than assuming the folder name.
        return Directory
            .EnumerateDirectories(outputDirectory)
            .FirstOrDefault(d => Directory.EnumerateFiles(d, "*.csproj").Any())
            ?? throw new InvalidOperationException(
                $"The template did not extract to a project folder under '{outputDirectory}'. " +
                "The template zip may be corrupt or use unsupported path separators.");
    }

    /// <summary>
    /// Extracts a template zip, treating '\' in entry names as a folder separator so zips written by
    /// Windows PowerShell 5.1 extract correctly on Linux. Entries that resolve outside the output
    /// directory are rejected (zip-slip guard).
    /// </summary>
    internal static void ExtractTemplateNormalized(string zipPath, string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(root, name));
            if (!destination.StartsWith(rootWithSeparator, comparison))
            {
                throw new InvalidDataException($"Template zip entry '{entry.FullName}' resolves outside the output directory.");
            }

            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
        }
    }

    private static string GetLanguageSubFolder(string fileName)
    {
        if (fileName.Equals("dependencies.json", StringComparison.OrdinalIgnoreCase))
        {
            return "CodeCSharp";
        }

        if (fileName.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase))
        {
            return "CodePython";
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".cs" => "CodeCSharp",
            ".nuspec" => "CodeCSharp",
            ".py" => "CodePython",
            _ => string.Empty // e.g. configuration.json lives at the Code/ root
        };
    }

    public async Task<byte[]> CreateProjectZipAsync(
        string projectName,
        Dictionary<string, string> codeFiles,
        int jobId = 0)
    {
        var tempParent = Path.Combine(Path.GetTempPath(), $"bdo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempParent);

        try
        {
            var templateZipPath = Path.Combine(_environment.ContentRootPath, "JobTemplate", "BlazorDataOrchestrator.JobCreatorTemplate.zip");

            if (!File.Exists(templateZipPath))
            {
                throw new FileNotFoundException("Template file not found.");
            }

            var outputDirectory = Path.Combine(tempParent, projectName);
            Directory.CreateDirectory(outputDirectory);

            ExtractTemplateNormalized(templateZipPath, outputDirectory);
            await ReplaceInFilesAndNamesAsync(outputDirectory, "JobCreatorTemplate", projectName);

            InjectCodeFiles(outputDirectory, codeFiles, jobId);

            var zipFilePath = Path.Combine(tempParent, $"{projectName}.zip");
            ZipFile.CreateFromDirectory(outputDirectory, zipFilePath, CompressionLevel.Optimal, includeBaseDirectory: true);
            return await File.ReadAllBytesAsync(zipFilePath);
        }
        finally
        {
            try { Directory.Delete(tempParent, true); } catch { }
        }
    }
}

public class ProjectCreationResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
}

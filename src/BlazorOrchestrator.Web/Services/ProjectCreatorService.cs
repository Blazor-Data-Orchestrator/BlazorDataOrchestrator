using System.IO.Compression;
using System.Text;

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
            ZipFile.ExtractToDirectory(templateZipPath, outputDirectory);

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
}

public class ProjectCreationResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
}

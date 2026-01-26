using System.IO.Compression;
using System.Text;
using BlazorDataOrchestrator.Core;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for creating NuGet packages from job code in the web context.
/// </summary>
public class WebNuGetPackageService
{
    private readonly ILogger<WebNuGetPackageService> _logger;
    private readonly JobManager _jobManager;
    private readonly EditorFileStorageService _fileStorage;

    public WebNuGetPackageService(
        ILogger<WebNuGetPackageService> logger,
        JobManager jobManager,
        EditorFileStorageService fileStorage)
    {
        _logger = logger;
        _jobManager = jobManager;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Creates a NuGet package from the job code model.
    /// </summary>
    /// <param name="codeModel">The job code model containing code and configuration.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The package version (auto-generated if not provided).</param>
    /// <param name="jobId">Optional job ID to include in configuration.json.</param>
    /// <returns>Tuple containing the package stream, filename, and version.</returns>
    public async Task<(MemoryStream PackageStream, string FileName, string Version)> CreatePackageAsync(
        JobCodeModel codeModel,
        string packageId = "BlazorDataOrchestrator.Job",
        string? version = null,
        int jobId = 0)
    {
        version ??= $"1.0.{DateTime.Now:yyyyMMddHHmmss}";
        
        // Add suffix for Python packages
        var suffix = codeModel.Language.ToLower() == "python" ? ".PYTHON" : "";
        var fullPackageId = $"{packageId}{suffix}";
        var fileName = $"{fullPackageId}.{version}.nupkg";

        _logger.LogInformation("Creating NuGet package: {FileName}", fileName);

        var packageStream = new MemoryStream();

        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, true))
        {
            // Add .nuspec file
            var nuspecContent = GenerateNuspec(fullPackageId, version, codeModel.Language);
            await AddEntryAsync(archive, $"{fullPackageId}.nuspec", nuspecContent);

            // Add code files based on language
            var contentBasePath = "contentFiles/any/any";
            var codeFolder = codeModel.Language.ToLower() == "python" ? "CodePython" : "CodeCSharp";
            var mainFileName = codeModel.Language.ToLower() == "python" ? "main.py" : "main.cs";

            if (codeModel.Language.ToLower() == "csharp")
            {
                await AddEntryAsync(archive, $"{contentBasePath}/{codeFolder}/{mainFileName}", codeModel.MainCode);
            }
            else if (codeModel.Language.ToLower() == "python")
            {
                await AddEntryAsync(archive, $"{contentBasePath}/{codeFolder}/{mainFileName}", codeModel.MainCode);
                
                if (!string.IsNullOrEmpty(codeModel.RequirementsTxt))
                {
                    await AddEntryAsync(archive, $"{contentBasePath}/{codeFolder}/requirements.txt", codeModel.RequirementsTxt);
                }
            }

            // Add additional code files
            foreach (var kvp in codeModel.AdditionalCodeFiles)
            {
                await AddEntryAsync(archive, $"{contentBasePath}/{codeFolder}/{kvp.Key}", kvp.Value);
            }

            // Add configuration.json with SelectedLanguage, LastJobId, and LastJobInstanceId
            var configJson = $"{{\"SelectedLanguage\": \"{codeModel.Language}\", \"LastJobId\": {jobId}, \"LastJobInstanceId\": 0}}";
            await AddEntryAsync(archive, $"{contentBasePath}/configuration.json", configJson);

            // Add appsettings files
            if (!string.IsNullOrEmpty(codeModel.AppSettings))
            {
                await AddEntryAsync(archive, $"{contentBasePath}/{codeFolder}/appsettings.json", codeModel.AppSettings);
            }

            if (!string.IsNullOrEmpty(codeModel.AppSettingsProduction))
            {
                await AddEntryAsync(archive, $"{contentBasePath}/{codeFolder}/appsettingsProduction.json", codeModel.AppSettingsProduction);
            }

            // Add [Content_Types].xml (required for NuGet)
            var contentTypesXml = GenerateContentTypesXml();
            await AddEntryAsync(archive, "[Content_Types].xml", contentTypesXml);

            // Add _rels/.rels (required for NuGet)
            var relsXml = GenerateRelsXml(fullPackageId);
            await AddEntryAsync(archive, "_rels/.rels", relsXml);
        }

        packageStream.Position = 0;
        
        _logger.LogInformation("Package created successfully: {FileName} ({Size} bytes)", 
            fileName, packageStream.Length);

        return (packageStream, fileName, version);
    }

    /// <summary>
    /// Uploads a package to blob storage and associates it with a job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="packageStream">The package stream.</param>
    /// <param name="fileName">The package filename.</param>
    /// <returns>The blob name of the uploaded package.</returns>
    public async Task<string> UploadPackageAsync(int jobId, MemoryStream packageStream, string fileName)
    {
        _logger.LogInformation("Uploading package {FileName} for job {JobId}", fileName, jobId);
        
        packageStream.Position = 0;
        var blobName = await _jobManager.UploadJobPackageAsync(jobId, packageStream, fileName);
        
        _logger.LogInformation("Package uploaded as: {BlobName}", blobName);
        
        return blobName;
    }

    /// <summary>
    /// Creates and uploads a package in one operation.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="codeModel">The job code model.</param>
    /// <returns>The blob name of the uploaded package.</returns>
    public async Task<string> CreateAndUploadPackageAsync(int jobId, JobCodeModel codeModel)
    {
        var (packageStream, fileName, _) = await CreatePackageAsync(codeModel, jobId: jobId);
        
        try
        {
            return await UploadPackageAsync(jobId, packageStream, fileName);
        }
        finally
        {
            await packageStream.DisposeAsync();
        }
    }

    private string GenerateNuspec(string packageId, string version, string language)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"">
  <metadata>
    <id>{packageId}</id>
    <version>{version}</version>
    <authors>BlazorDataOrchestrator</authors>
    <description>Auto-generated job package ({language})</description>
    <contentFiles>
      <files include=""**/*"" buildAction=""Content"" copyToOutput=""true"" />
    </contentFiles>
  </metadata>
</package>";
    }

    private string GenerateContentTypesXml()
    {
        return @"<?xml version=""1.0"" encoding=""utf-8""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml"" />
  <Default Extension=""nuspec"" ContentType=""application/octet"" />
  <Default Extension=""cs"" ContentType=""text/plain"" />
  <Default Extension=""py"" ContentType=""text/plain"" />
  <Default Extension=""json"" ContentType=""application/json"" />
  <Default Extension=""txt"" ContentType=""text/plain"" />
</Types>";
    }

    private string GenerateRelsXml(string packageId)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Type=""http://schemas.microsoft.com/packaging/2010/07/manifest"" Target=""/{packageId}.nuspec"" Id=""R1"" />
</Relationships>";
    }

    private async Task AddEntryAsync(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        await writer.WriteAsync(content);
    }
}

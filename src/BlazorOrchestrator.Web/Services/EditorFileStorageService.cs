namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for storing code files in memory for the editor.
/// Each job has its own set of files stored temporarily during editing.
/// </summary>
public class EditorFileStorageService
{
    private readonly Dictionary<int, Dictionary<string, string>> _jobFiles = new();
    private readonly Dictionary<int, EditorState> _editorStates = new();
    private readonly Dictionary<int, List<NuGetDependencyInfo>> _jobDependencies = new();
    private readonly Dictionary<int, string?> _jobNuspecContent = new();
    private readonly Dictionary<int, string?> _jobNuspecFileName = new();
    private readonly object _lock = new();

    /// <summary>
    /// Sets the content of a file for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="content">The file content.</param>
    public void SetFile(int jobId, string fileName, string content)
    {
        lock (_lock)
        {
            if (!_jobFiles.ContainsKey(jobId))
            {
                _jobFiles[jobId] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            _jobFiles[jobId][fileName] = content;
        }
    }

    /// <summary>
    /// Gets the content of a file for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>The file content, or null if not found.</returns>
    public string? GetFile(int jobId, string fileName)
    {
        lock (_lock)
        {
            if (_jobFiles.TryGetValue(jobId, out var files))
            {
                return files.TryGetValue(fileName, out var content) ? content : null;
            }
            return null;
        }
    }

    /// <summary>
    /// Gets all files for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <returns>Dictionary of file names and their content.</returns>
    public Dictionary<string, string> GetAllFiles(int jobId)
    {
        lock (_lock)
        {
            return _jobFiles.TryGetValue(jobId, out var files)
                ? new Dictionary<string, string>(files, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Checks if a file exists for a job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>True if the file exists.</returns>
    public bool FileExists(int jobId, string fileName)
    {
        lock (_lock)
        {
            return _jobFiles.TryGetValue(jobId, out var files) && files.ContainsKey(fileName);
        }
    }

    /// <summary>
    /// Deletes a file for a job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="fileName">The file name.</param>
    public void DeleteFile(int jobId, string fileName)
    {
        lock (_lock)
        {
            if (_jobFiles.TryGetValue(jobId, out var files))
            {
                files.Remove(fileName);
            }
        }
    }

    /// <summary>
    /// Clears all files for a job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    public void ClearFiles(int jobId)
    {
        lock (_lock)
        {
            _jobFiles.Remove(jobId);
            _editorStates.Remove(jobId);
            _jobDependencies.Remove(jobId);
            _jobNuspecContent.Remove(jobId);
            _jobNuspecFileName.Remove(jobId);
        }
    }

    /// <summary>
    /// Sets the NuGet dependencies for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="dependencies">The list of NuGet dependencies.</param>
    public void SetDependencies(int jobId, List<NuGetDependencyInfo> dependencies)
    {
        lock (_lock)
        {
            _jobDependencies[jobId] = new List<NuGetDependencyInfo>(dependencies);
        }
    }

    /// <summary>
    /// Gets the NuGet dependencies for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <returns>List of NuGet dependencies, or empty list if not found.</returns>
    public List<NuGetDependencyInfo> GetDependencies(int jobId)
    {
        lock (_lock)
        {
            return _jobDependencies.TryGetValue(jobId, out var deps)
                ? new List<NuGetDependencyInfo>(deps)
                : new List<NuGetDependencyInfo>();
        }
    }

    /// <summary>
    /// Sets the .nuspec content and filename for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="content">The .nuspec file content.</param>
    /// <param name="fileName">The .nuspec file name.</param>
    public void SetNuspecContent(int jobId, string? content, string? fileName)
    {
        lock (_lock)
        {
            _jobNuspecContent[jobId] = content;
            _jobNuspecFileName[jobId] = fileName;
        }
    }

    /// <summary>
    /// Gets the .nuspec content and filename for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <returns>Tuple containing the content and filename.</returns>
    public (string? Content, string? FileName) GetNuspecInfo(int jobId)
    {
        lock (_lock)
        {
            _jobNuspecContent.TryGetValue(jobId, out var content);
            _jobNuspecFileName.TryGetValue(jobId, out var fileName);
            return (content, fileName);
        }
    }

    /// <summary>
    /// Gets the file names for a job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <returns>List of file names.</returns>
    public List<string> GetFileNames(int jobId)
    {
        lock (_lock)
        {
            return _jobFiles.TryGetValue(jobId, out var files)
                ? files.Keys.ToList()
                : new List<string>();
        }
    }

    /// <summary>
    /// Saves the editor state for a job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="state">The editor state.</param>
    public void SetEditorState(int jobId, EditorState state)
    {
        lock (_lock)
        {
            _editorStates[jobId] = state;
        }
    }

    /// <summary>
    /// Gets the editor state for a job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <returns>The editor state, or null if not found.</returns>
    public EditorState? GetEditorState(int jobId)
    {
        lock (_lock)
        {
            return _editorStates.TryGetValue(jobId, out var state) ? state : null;
        }
    }

    /// <summary>
    /// Initializes files from a JobCodeModel.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="codeModel">The code model to initialize from.</param>
    public void InitializeFromCodeModel(int jobId, JobCodeModel codeModel)
    {
        lock (_lock)
        {
            ClearFiles(jobId);

            if (codeModel.Language.ToLower() == "csharp")
            {
                SetFile(jobId, "main.cs", codeModel.MainCode);
            }
            else if (codeModel.Language.ToLower() == "python")
            {
                SetFile(jobId, "main.py", codeModel.MainCode);
                if (!string.IsNullOrEmpty(codeModel.RequirementsTxt))
                {
                    SetFile(jobId, "requirements.txt", codeModel.RequirementsTxt);
                }
            }

            SetFile(jobId, "appsettings.json", codeModel.AppSettings);
            SetFile(jobId, "appsettings.Production.json", codeModel.AppSettingsProduction);

            // Add any additional code files
            foreach (var kvp in codeModel.AdditionalCodeFiles)
            {
                SetFile(jobId, kvp.Key, kvp.Value);
            }

            // Store dependencies and nuspec content for C# packages
            if (codeModel.Dependencies?.Any() == true)
            {
                _jobDependencies[jobId] = new List<NuGetDependencyInfo>(codeModel.Dependencies);
            }
            
            if (!string.IsNullOrEmpty(codeModel.NuspecContent))
            {
                _jobNuspecContent[jobId] = codeModel.NuspecContent;
                _jobNuspecFileName[jobId] = codeModel.NuspecFileName;
            }

            // Determine the selected file
            var selectedFile = codeModel.Language.ToLower() == "csharp" ? "main.cs" : "main.py";
            
            // If we have discovered files, use that order
            if (codeModel.DiscoveredFiles.Count > 0)
            {
                selectedFile = codeModel.DiscoveredFiles.First();
            }

            SetEditorState(jobId, new EditorState
            {
                Language = codeModel.Language,
                SelectedFile = selectedFile
            });
        }
    }

    /// <summary>
    /// Converts stored files to a JobCodeModel.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <returns>The job code model.</returns>
    public JobCodeModel ToCodeModel(int jobId)
    {
        lock (_lock)
        {
            var state = GetEditorState(jobId);
            var language = state?.Language ?? "csharp";

            var model = new JobCodeModel
            {
                Language = language,
                AppSettings = GetFile(jobId, "appsettings.json") ?? "{}",
                AppSettingsProduction = GetFile(jobId, "appsettings.Production.json") ?? "{}"
            };

            // Get all files for this job
            var allFiles = GetAllFiles(jobId);
            var codeExtension = language.ToLower() == "python" ? ".py" : ".cs";
            var mainFileName = language.ToLower() == "python" ? "main.py" : "main.cs";

            if (language.ToLower() == "csharp")
            {
                model.MainCode = GetFile(jobId, "main.cs") ?? "";
            }
            else if (language.ToLower() == "python")
            {
                model.MainCode = GetFile(jobId, "main.py") ?? "";
                model.RequirementsTxt = GetFile(jobId, "requirements.txt");
            }

            // Add additional code files (excluding main file and config files)
            foreach (var kvp in allFiles)
            {
                var lowerFileName = kvp.Key.ToLowerInvariant();
                
                // Skip main file, config files, and requirements.txt
                if (lowerFileName == mainFileName.ToLower() ||
                    lowerFileName == "appsettings.json" ||
                    lowerFileName == "appsettings.production.json" ||
                    lowerFileName == "requirements.txt")
                {
                    continue;
                }

                // Add code files to additional files
                if (kvp.Key.EndsWith(codeExtension, StringComparison.OrdinalIgnoreCase))
                {
                    model.AdditionalCodeFiles[kvp.Key] = kvp.Value;
                }
            }

            // Build discovered files list in proper order
            model.DiscoveredFiles.Add(mainFileName);
            model.DiscoveredFiles.Add("appsettings.json");
            model.DiscoveredFiles.Add("appsettings.Production.json");
            
            if (language.ToLower() == "python" && !string.IsNullOrEmpty(model.RequirementsTxt))
            {
                model.DiscoveredFiles.Add("requirements.txt");
            }

            foreach (var fileName in model.AdditionalCodeFiles.Keys)
            {
                model.DiscoveredFiles.Add(fileName);
            }

            // Include dependencies and nuspec content from storage
            model.Dependencies = GetDependencies(jobId);
            var (nuspecContent, nuspecFileName) = GetNuspecInfo(jobId);
            model.NuspecContent = nuspecContent;
            model.NuspecFileName = nuspecFileName;
            
            // Add .nuspec to discovered files if available (for C#)
            if (!string.IsNullOrEmpty(nuspecFileName) && language.ToLower() == "csharp")
            {
                if (!model.DiscoveredFiles.Contains(nuspecFileName))
                {
                    model.DiscoveredFiles.Add(nuspecFileName);
                }
            }

            return model;
        }
    }
}

/// <summary>
/// Represents the current state of the code editor for a job.
/// </summary>
public class EditorState
{
    /// <summary>
    /// The currently selected programming language.
    /// </summary>
    public string Language { get; set; } = "csharp";

    /// <summary>
    /// The currently selected file.
    /// </summary>
    public string? SelectedFile { get; set; }

    /// <summary>
    /// Indicates whether the code has unsaved changes.
    /// </summary>
    public bool HasUnsavedChanges { get; set; }

    /// <summary>
    /// The last time the code was saved.
    /// </summary>
    public DateTime? LastSaved { get; set; }
}

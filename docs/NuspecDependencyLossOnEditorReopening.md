# NuSpec Dependency Loss on Editor Reopening Issue

## Issue Summary

When clicking the **Editor** button on the Code tab to edit an existing NuGet package that contains NuGet dependencies (e.g., SendGrid), the `.nuspec` file becomes incorrect (missing dependencies) on subsequent edits.

### Reproduction Steps

1. Upload a NuGet package with dependencies (e.g., SendGrid) to a job
2. Switch to **Code Edit** mode (Editor button) - **Works correctly** the first time
3. Edit and save the code (Save & Compile)
4. Switch back to **Code Upload** mode
5. Switch to **Code Edit** mode again
6. **BUG**: The `.nuspec` now shows **no dependencies** - SendGrid and other references are missing

### Expected Behavior

The NuGet dependencies should be preserved when saving and reloading the package through multiple Editor mode sessions.

---

## Root Cause Analysis

### Data Flow During First Load (Works)

```
1. User uploads package with dependencies
   └─> Package stored in blob storage with correct .nuspec

2. User clicks "Editor" button
   └─> LoadPackageFilesAsync() downloads package
       └─> ExtractAllFilesFromPackageAsync() extracts .nuspec
           └─> ParseNuSpecDependencies() populates loadedCodeModel.Dependencies
               └─> Dependencies: [SendGrid v9.x, etc.]

3. User edits code and clicks "Save & Compile"
   └─> loadedCodeModel.Dependencies is used for compilation ✅
```

### Data Flow During Save (Bug Origin)

```
4. SaveAndCompileCode() creates new package:
   └─> var codeModel = FileStorage.ToCodeModel(JobId);
       └─> ToCodeModel() creates NEW JobCodeModel
           └─> ❌ Does NOT copy Dependencies from loadedCodeModel
           └─> ❌ Does NOT copy NuspecContent from loadedCodeModel
   
   └─> PackageService.CreateAndUploadPackageAsync(Job.Id, codeModel);
       └─> GenerateNuspec(..., codeModel.Dependencies)  // Dependencies is EMPTY!
           └─> Creates .nuspec WITHOUT dependency elements
   
   └─> New package uploaded to blob storage (missing dependencies)
```

### Data Flow During Second Load (Bug Manifests)

```
5. User switches to "Code Upload" mode, then back to "Editor"
   └─> hasLoadedPackageFiles = false (reset after save)
   └─> LoadPackageFilesAsync() downloads the NEW package
       └─> ExtractAllFilesFromPackageAsync() extracts .nuspec
           └─> ParseNuSpecDependencies() finds NO dependencies
               └─> loadedCodeModel.Dependencies = [] ❌
```

---

## Affected Files

| File | Role | Issue |
|------|------|-------|
| [EditorFileStorageService.cs](../src/BlazorOrchestrator.Web/Services/EditorFileStorageService.cs) | Stores editor files in memory | `ToCodeModel()` doesn't preserve Dependencies or NuspecContent |
| [EditorFileStorageService.cs](../src/BlazorOrchestrator.Web/Services/EditorFileStorageService.cs) | Stores editor state | `InitializeFromCodeModel()` doesn't store Dependencies |
| [JobDetailsDialog.razor](../src/BlazorOrchestrator.Web/Components/Pages/Dialogs/JobDetailsDialog.razor) | Code editor UI | Uses `FileStorage.ToCodeModel()` which loses dependencies |
| [WebNuGetPackageService.cs](../src/BlazorOrchestrator.Web/Services/WebNuGetPackageService.cs) | Creates NuGet packages | Receives empty dependencies from incomplete codeModel |

---

## Proposed Solution

### Option A: Store Dependencies in EditorFileStorageService (Recommended)

Extend `EditorFileStorageService` to track NuGet dependencies alongside files.

#### Phase 1: Extend EditorFileStorageService

**File:** `src/BlazorOrchestrator.Web/Services/EditorFileStorageService.cs`

1. Add dependency storage:
   ```csharp
   private readonly Dictionary<int, List<NuGetDependencyInfo>> _jobDependencies = new();
   private readonly Dictionary<int, string?> _jobNuspecContent = new();
   private readonly Dictionary<int, string?> _jobNuspecFileName = new();
   ```

2. Add methods to get/set dependencies:
   ```csharp
   public void SetDependencies(int jobId, List<NuGetDependencyInfo> dependencies)
   {
       lock (_lock)
       {
           _jobDependencies[jobId] = new List<NuGetDependencyInfo>(dependencies);
       }
   }

   public List<NuGetDependencyInfo> GetDependencies(int jobId)
   {
       lock (_lock)
       {
           return _jobDependencies.TryGetValue(jobId, out var deps) 
               ? new List<NuGetDependencyInfo>(deps) 
               : new List<NuGetDependencyInfo>();
       }
   }

   public void SetNuspecContent(int jobId, string? content, string? fileName)
   {
       lock (_lock)
       {
           _jobNuspecContent[jobId] = content;
           _jobNuspecFileName[jobId] = fileName;
       }
   }

   public (string? Content, string? FileName) GetNuspecInfo(int jobId)
   {
       lock (_lock)
       {
           _jobNuspecContent.TryGetValue(jobId, out var content);
           _jobNuspecFileName.TryGetValue(jobId, out var fileName);
           return (content, fileName);
       }
   }
   ```

3. Update `InitializeFromCodeModel()` to store dependencies:
   ```csharp
   public void InitializeFromCodeModel(int jobId, JobCodeModel codeModel)
   {
       lock (_lock)
       {
           ClearFiles(jobId);
           
           // ... existing file initialization code ...
           
           // Store dependencies and nuspec info
           if (codeModel.Dependencies?.Any() == true)
           {
               _jobDependencies[jobId] = new List<NuGetDependencyInfo>(codeModel.Dependencies);
           }
           
           _jobNuspecContent[jobId] = codeModel.NuspecContent;
           _jobNuspecFileName[jobId] = codeModel.NuspecFileName;
       }
   }
   ```

4. Update `ToCodeModel()` to include dependencies:
   ```csharp
   public JobCodeModel ToCodeModel(int jobId)
   {
       lock (_lock)
       {
           // ... existing code ...
           
           // Include dependencies
           model.Dependencies = GetDependencies(jobId);
           
           // Include nuspec info
           var (nuspecContent, nuspecFileName) = GetNuspecInfo(jobId);
           model.NuspecContent = nuspecContent;
           model.NuspecFileName = nuspecFileName;
           
           return model;
       }
   }
   ```

5. Update `ClearFiles()` to clear dependencies:
   ```csharp
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
   ```

#### Phase 2: Update Dependency Storage When Editing .nuspec

**File:** `src/BlazorOrchestrator.Web/Components/Pages/Dialogs/JobDetailsDialog.razor`

If the user edits the `.nuspec` file directly, the dependencies should be re-parsed:

```csharp
// In the file save/switch logic
if (selectedCodeFile?.EndsWith(".nuspec") == true && loadedCodeModel != null)
{
    // Update nuspec content
    loadedCodeModel.NuspecContent = currentJobCode;
    
    // Re-parse dependencies from the edited .nuspec
    loadedCodeModel.Dependencies = CodeEditorService.ParseNuSpecDependencies(currentJobCode);
    
    // Update file storage
    FileStorage.SetNuspecContent(JobId, currentJobCode, selectedCodeFile);
    FileStorage.SetDependencies(JobId, loadedCodeModel.Dependencies);
}
```

---

### Option B: Pass loadedCodeModel Directly to Package Service

Alternative: Instead of using `FileStorage.ToCodeModel()`, pass the `loadedCodeModel` with updated file contents directly.

**Pros:** Less code changes
**Cons:** Requires ensuring `loadedCodeModel` is always up-to-date with file changes

---

## Implementation Plan

| Phase | Task | Status | Estimated Effort |
|-------|------|--------|------------------|
| 1 | Add dependency storage to EditorFileStorageService | ✅ Completed | 1 hour |
| 2 | Update InitializeFromCodeModel() | ✅ Completed | 30 min |
| 3 | Update ToCodeModel() | ✅ Completed | 30 min |
| 4 | Update ClearFiles() | ✅ Completed | 15 min |
| 5 | Handle .nuspec editing in JobDetailsDialog | ✅ Completed | 45 min |
| 6 | Add unit tests | Not Started | 1 hour |
| 7 | Manual testing with SendGrid package | Not Started | 30 min |

---

## Testing Strategy

### Test Case 1: Basic Dependency Preservation

1. Upload package with SendGrid dependency
2. Switch to Editor mode
3. Verify dependencies are shown in console logs
4. Save and compile
5. Switch to Upload mode, then back to Editor
6. **Verify**: Dependencies still present

### Test Case 2: Edit .nuspec Directly

1. Load package with dependencies
2. Select `.nuspec` in dropdown
3. Add a new dependency manually
4. Save and compile
5. Reload Editor
6. **Verify**: New dependency is preserved

### Test Case 3: Multiple Dependencies

1. Upload package with multiple dependencies (SendGrid, Newtonsoft.Json, etc.)
2. Complete round-trip through Editor
3. **Verify**: All dependencies preserved

---

## Related Documentation

- [CodeTabCompilationReferencesIssue.md](CodeTabCompilationReferencesIssue.md) - Original compilation references issue
- [OnlineCodeEditorFeaturePlan.md](OnlineCodeEditorFeaturePlan.md) - Online code editor feature plan
- [NuGetExecutionFeature.md](NuGetExecutionFeature.md) - NuGet execution feature details

---

## Appendix: Current Code References

### EditorFileStorageService.ToCodeModel() (Current - Missing Dependencies)

```csharp
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

        // ... file handling code ...

        // ❌ Missing: model.Dependencies = GetDependencies(jobId);
        // ❌ Missing: model.NuspecContent = GetNuspecContent(jobId);
        
        return model;
    }
}
```

### WebNuGetPackageService.GenerateNuspec() (Depends on Populated Dependencies)

```csharp
private string GenerateNuspec(string packageId, string version, string language, 
    List<NuGetDependencyInfo>? dependencies = null)
{
    var dependenciesXml = "";
    
    if (dependencies?.Any() == true)  // ❌ Empty because ToCodeModel() didn't include them
    {
        var depElements = string.Join("\n      ", 
            dependencies.Select(d => $@"<dependency id=""{d.PackageId}"" version=""{d.Version}"" />"));
        
        dependenciesXml = $@"
    <dependencies>
      <group targetFramework=""net10.0"">
      {depElements}
      </group>
    </dependencies>";
    }
    
    // ... rest of nuspec generation ...
}
```

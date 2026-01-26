# Code Tab Compilation References Issue

## Issue Summary

**Date Identified:** January 25, 2026  
**Status:** ✅ Completed  
**Priority:** High  
**Component:** BlazorOrchestrator.Web - Code Editor Tab

### Problem Description

The "Save & Compile" and "Run Job Now" buttons on the **Code tab** of the `JobDetailsDialog.razor` show compilation errors when executing code, while the "Run Job Now" button on the **Details tab** does not show any errors when running the exact same code.

### Recent Updates

**January 25, 2026:** 
- ✅ Updated `JobCodeModel` to include `NuspecContent` and `NuspecFileName` properties
- ✅ Updated `ExtractAllFilesFromPackageAsync()` to extract `.nuspec` files for C# packages
- ✅ Updated `GetFileContent()` and `SetFileContent()` to handle `.nuspec` files
- ✅ Updated `GetMonacoLanguageForFile()` to recognize `.nuspec` as XML
- ✅ Updated `UpdateFileListForLanguage()` in `JobDetailsDialog.razor` to show `.nuspec` in dropdown for C#

### Root Cause Analysis

#### Comparison of Execution Paths

| Feature | Details Tab (`RunJobNow`) | Code Tab (`RunJobWithCode`) |
|---------|---------------------------|------------------------------|
| Compilation | ❌ No pre-compilation | ✅ Compiles code locally first |
| Error Display | ❌ None (errors surface later in Agent) | ✅ Shows compilation errors dialog |
| Code Validation | ❌ None | ✅ Uses `CSharpCompilationService.Compile()` |
| Reference Loading | N/A | ❌ **Missing NuGet references** |
| .nuspec Visibility | N/A | ✅ **Now visible in Code tab dropdown** |

#### The Core Issue

The `CSharpCompilationService.Compile()` method (used by the Code tab) only loads basic runtime references:

```csharp
// Current reference loading in CSharpCompilationService.GetMetadataReferences()
var coreAssemblies = new[]
{
    typeof(object).Assembly,
    typeof(Console).Assembly,
    typeof(Task).Assembly,
    typeof(Enumerable).Assembly,
    typeof(List<>).Assembly
};

var assemblyNames = new[]
{
    "System.Runtime",
    "System.Collections",
    "System.Linq",
    "System.Threading.Tasks",
    "netstandard",
    "System.Console",
    "System.Text.Json"
};
```

**However**, the actual code in the NuGet package may reference additional assemblies that are:
1. Defined in the package's `.nuspec` file as dependencies
2. Included as DLLs in the package itself
3. Referenced by the `BlazorDataOrchestrator.Core` assembly

#### Why Agent Execution Works

The `CodeExecutorService` in the Agent properly resolves all references:

1. **Parses `.nuspec` dependencies**: `_packageProcessor.GetDependenciesFromNuSpecAsync(extractedPath)`
2. **Resolves NuGet packages**: `_nugetResolver.ResolveAsync(dependencies, targetFramework)`
3. **Loads package DLLs**: Scans `extractedPath` for `*.dll` files
4. **Adds common references**: `EntityFrameworkCore`, `BlazorDataOrchestrator.Core`, etc.

#### Why LoadPackageFilesAsync May Be Incomplete

The `LoadPackageFilesAsync()` method in `JobDetailsDialog.razor` extracts files from the package but **does not**:
1. ~~Parse the `.nuspec` file for NuGet dependencies~~ ✅ **Now extracts and displays .nuspec content**
2. Extract or track DLL files in the package
3. Pass dependency information to the compilation service

```csharp
// Current extraction now gets code files AND .nuspec for C#
loadedCodeModel = await CodeEditorService.ExtractAllFilesFromPackageAsync(
    packageStream, codeLanguage);
    
// .nuspec content is now available in:
// - loadedCodeModel.NuspecContent
// - loadedCodeModel.NuspecFileName
// - Visible in the file dropdown when language is C#
```

The `ExtractAllFilesFromPackageAsync()` method now:
- ✅ Extracts `.nuspec` files for C# packages
- ✅ Stores content in `JobCodeModel.NuspecContent`
- ✅ Adds to `DiscoveredFiles` list for dropdown display
- ❌ Still filters out DLL files
- ❌ Does not parse `.nuspec` for dependency resolution

---

## Proposed Solution - Remaining Work

### Phase 1: ✅ COMPLETED - Display .nuspec in Code Tab

**Files Modified:**
- `src/BlazorOrchestrator.Web/Services/JobCodeEditorService.cs`
  - Added `NuspecContent` and `NuspecFileName` properties to `JobCodeModel`
  - Updated `ExtractAllFilesFromPackageAsync()` to extract `.nuspec` for C#
  - Updated `GetFileContent()` and `SetFileContent()` to handle `.nuspec`
  - Updated `GetMonacoLanguageForFile()` to recognize `.nuspec` as XML

- `src/BlazorOrchestrator.Web/Components/Pages/Dialogs/JobDetailsDialog.razor`
  - Updated `UpdateFileListForLanguage()` to include `.nuspec` in dropdown for C#

### Phase 2: Parse .nuspec Dependencies

**File:** `src/BlazorOrchestrator.Web/Services/JobCodeEditorService.cs`

1. Add new classes to parse `.nuspec` XML:
   ```csharp
   public class NuGetDependencyInfo
   {
       public string PackageId { get; set; } = "";
       public string Version { get; set; } = "";
       public string TargetFramework { get; set; } = "";
   }
   ```

2. Add `Dependencies` property to `JobCodeModel`:
   ```csharp
   public List<NuGetDependencyInfo> Dependencies { get; set; } = new();
   ```

3. Create `ParseNuSpecDependencies()` method:
   ```csharp
   private List<NuGetDependencyInfo> ParseNuSpecDependencies(string nuspecContent)
   {
       var dependencies = new List<NuGetDependencyInfo>();
       try
       {
           var doc = XDocument.Parse(nuspecContent);
           var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
           
           var dependencyElements = doc.Descendants(ns + "dependency");
           foreach (var dep in dependencyElements)
           {
               dependencies.Add(new NuGetDependencyInfo
               {
                   PackageId = dep.Attribute("id")?.Value ?? "",
                   Version = dep.Attribute("version")?.Value ?? "",
                   TargetFramework = dep.Parent?.Attribute("targetFramework")?.Value ?? ""
               });
           }
       }
       catch (Exception ex)
       {
           _logger.LogWarning(ex, "Failed to parse .nuspec dependencies");
       }
       return dependencies;
   }
   ```

4. Update `ExtractAllFilesFromPackageAsync()` to call `ParseNuSpecDependencies()`:
   ```csharp
   if (lowerPath.EndsWith(".nuspec"))
   {
       using var nuspecReader = new StreamReader(entry.Open());
       model.NuspecContent = await nuspecReader.ReadToEndAsync();
       model.NuspecFileName = fileName;
       model.Dependencies = ParseNuSpecDependencies(model.NuspecContent);
       // ... rest of existing code
   }
   ```

### Phase 3: Create WebNuGetResolverService

**File:** `src/BlazorOrchestrator.Web/Services/WebNuGetResolverService.cs` (New)

Create a service to resolve NuGet package dependencies for compilation:

```csharp
using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Resolves NuGet package dependencies for web-based compilation.
/// Caches resolved assemblies to minimize repeated downloads.
/// </summary>
public class WebNuGetResolverService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebNuGetResolverService> _logger;
    private static readonly ConcurrentDictionary<string, MetadataReference?> _referenceCache = new();
    
    public WebNuGetResolverService(
        IHttpClientFactory httpClientFactory, 
        ILogger<WebNuGetResolverService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }
    
    /// <summary>
    /// Resolves dependencies to MetadataReferences for compilation.
    /// </summary>
    public async Task<List<MetadataReference>> ResolveForCompilationAsync(
        List<NuGetDependencyInfo> dependencies,
        string targetFramework = "net9.0")
    {
        var references = new List<MetadataReference>();
        
        foreach (var dep in dependencies)
        {
            var cacheKey = $"{dep.PackageId}:{dep.Version}";
            
            if (_referenceCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                references.Add(cached);
                continue;
            }
            
            try
            {
                var reference = await DownloadPackageReferenceAsync(dep);
                if (reference != null)
                {
                    _referenceCache[cacheKey] = reference;
                    references.Add(reference);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve {PackageId} v{Version}", 
                    dep.PackageId, dep.Version);
            }
        }
        
        return references;
    }
    
    private async Task<MetadataReference?> DownloadPackageReferenceAsync(NuGetDependencyInfo dep)
    {
        // Download from NuGet.org and extract primary DLL
        // Implementation details...
        return null;
    }
}
```

### Phase 4: Update CSharpCompilationService

**File:** `src/BlazorOrchestrator.Web/Services/CSharpCompilationService.cs`

1. Add dependency injection for `WebNuGetResolverService`
2. Add method overload that accepts dependencies:
   ```csharp
   public async Task<CompilationResult> CompileWithDependenciesAsync(
       string code, 
       List<NuGetDependencyInfo>? dependencies = null,
       string assemblyName = "JobAssembly")
   {
       var additionalReferences = new List<MetadataReference>();
       
       if (dependencies?.Any() == true)
       {
           additionalReferences = await _nugetResolver.ResolveForCompilationAsync(dependencies);
       }
       
       // Add BlazorDataOrchestrator.Core reference (always needed)
       additionalReferences.Add(MetadataReference.CreateFromFile(
           typeof(BlazorDataOrchestrator.Core.JobManager).Assembly.Location));
       
       return Compile(code, assemblyName, additionalReferences);
   }
   ```

3. Add commonly required references:
   ```csharp
   // Standard references that job code typically needs
   private void AddStandardReferences(List<MetadataReference> references)
   {
       // BlazorDataOrchestrator.Core
       references.Add(MetadataReference.CreateFromFile(
           typeof(BlazorDataOrchestrator.Core.JobManager).Assembly.Location));
       
       // Microsoft.EntityFrameworkCore
       references.Add(MetadataReference.CreateFromFile(
           typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.Location));
       
       // System.Net.Http
       references.Add(MetadataReference.CreateFromFile(
           typeof(System.Net.Http.HttpClient).Assembly.Location));
       
       // System.Net.Http.Json
       references.Add(MetadataReference.CreateFromFile(
           typeof(System.Net.Http.Json.HttpClientJsonExtensions).Assembly.Location));
   }
   ```

### Phase 5: Update JobDetailsDialog.razor

**File:** `src/BlazorOrchestrator.Web/Components/Pages/Dialogs/JobDetailsDialog.razor`

1. Inject `WebNuGetResolverService`
2. Update `SaveAndCompileCode()`:
   ```csharp
   // Use dependency-aware compilation
   if (loadedCodeModel?.Dependencies?.Any() == true)
   {
       compilationResult = await CSharpCompiler.CompileWithDependenciesAsync(
           mainCode, loadedCodeModel.Dependencies);
   }
   else
   {
       compilationResult = CSharpCompiler.Compile(mainCode);
   }
   ```

3. Update `RunJobWithCode()` similarly

### Phase 6: Register Services

**File:** `src/BlazorOrchestrator.Web/Program.cs`

```csharp
builder.Services.AddHttpClient();
builder.Services.AddScoped<WebNuGetResolverService>();
```

---

## Implementation Order (Revised)

| Phase | Description | Status | Risk |
|-------|-------------|--------|------|
| 1 | Display .nuspec in Code Tab | ✅ Completed | Low |
| 2 | Parse .nuspec Dependencies | ✅ Completed | Low |
| 3 | Create WebNuGetResolverService | ✅ Completed | High |
| 4 | Update CSharpCompilationService | ✅ Completed | Medium |
| 5 | Update JobDetailsDialog.razor | ✅ Completed | Low |
| 6 | Register Services | ✅ Completed | Low |

**Recommended implementation sequence:**
1. **Phase 2** - Parse dependencies (foundation for other phases)
2. **Phase 4** - Add standard references first (quick win, immediate improvement)
3. **Phase 3** - Create WebNuGetResolverService (most complex)
4. **Phase 5** - Update dialog methods
5. **Phase 6** - Register services

---

## Files to Modify

| File | Action | Description | Status |
|------|--------|-------------|--------|
| `src/BlazorOrchestrator.Web/Services/JobCodeEditorService.cs` | Modify | Add `.nuspec` extraction, dependency parsing | ✅ Completed |
| `src/BlazorOrchestrator.Web/Services/CSharpCompilationService.cs` | Modify | Add dependency-aware compilation, standard references | ✅ Completed |
| `src/BlazorOrchestrator.Web/Services/WebNuGetResolverService.cs` | Create | New service for web-compatible NuGet resolution | ✅ Completed |
| `src/BlazorOrchestrator.Web/Components/Pages/Dialogs/JobDetailsDialog.razor` | Modify | Pass dependency info to compiler | ✅ Completed |
| `src/BlazorOrchestrator.Web/Program.cs` | Modify | Register new services | ✅ Completed |

---

## Testing Plan

1. **Unit Tests**:
   - Test `.nuspec` parsing
   - Test DLL extraction from packages
   - Test compilation with external references

2. **Integration Tests**:
   - Upload package with dependencies → Edit in Code tab → Save & Compile
   - Verify same errors (or lack thereof) between Details tab and Code tab

3. **Manual Testing**:
   - Create job with `Microsoft.EntityFrameworkCore` dependency
   - Test compilation on Code tab
   - Run job via Details tab
   - Compare results
   - **NEW:** Verify `.nuspec` file appears in dropdown for C# jobs
   - **NEW:** Verify `.nuspec` content is readable and editable in Monaco editor

---

## Acceptance Criteria

### Phase 1 (Completed)
- [x] `.nuspec` file appears in file dropdown when C# language is selected
- [x] `.nuspec` content is displayed correctly in Monaco editor with XML syntax highlighting
- [x] `.nuspec` content can be read from `JobCodeModel.NuspecContent`
- [x] Switching between files preserves `.nuspec` content

### Future Phases
- [x] Dependencies are parsed from `.nuspec` XML
- [x] Parsed dependencies are available in `JobCodeModel.Dependencies`
- [x] "Save & Compile" uses dependencies to resolve additional references
- [ ] "Run Job Now" on Code tab produces same results as Details tab
- [ ] Compilation errors match agent execution errors

---

## Related Documentation

- [OnlineCodeEditorFeaturePlan.md](OnlineCodeEditorFeaturePlan.md)
- [OnlineJobEditorEnhancements.md](OnlineJobEditorEnhancements.md)
- [NuGetExecutionFeature.md](NuGetExecutionFeature.md)
- [NestedNuGetDependencyResolutionPlan.md](NestedNuGetDependencyResolutionPlan.md)

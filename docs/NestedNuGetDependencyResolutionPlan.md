# Implementation Plan: Nested NuGet Package Dependency Resolution

## Status: ✅ Implemented

---

## Problem Statement

When executing a Job with a C# NuGet package that references other NuGet packages (e.g., SendGrid, Azure.Storage.Blobs, etc.), the dependencies declared in the `.nuspec` file are not being resolved and loaded at runtime. This results in compilation errors like:

```
error CS0246: The type or namespace name 'SendGrid' could not be found (are you missing a using directive or an assembly reference?)
```

### Root Cause Analysis

The current implementation in [CodeExecutorService.cs](../src/BlazorDataOrchestrator.Core/Services/CodeExecutorService.cs) uses CS-Script's hosted script execution. According to the [CS-Script Hosted Script Execution documentation](https://github.com/oleg-shilo/cs-script/wiki/Hosted-Script-Execution#referencing-nuget-packages):

> **"Executing hosted scripts with CS-Script follows the very same paradigm. The host application is responsible for preparing the script dependencies (ensuring the availability of NuGet packages and assemblies). That's why if you have `//css_nuget <package-name>` in your hosted script, it will be ignored."**

This means:
1. CS-Script's hosted execution mode **does NOT** automatically resolve NuGet packages
2. The `//css_nuget` directive only works in CLI mode, not hosted mode
3. The host application (our Agent) must manually download and reference all dependent assemblies

### Current Behavior

1. The NuGet package is downloaded from Azure Blob Storage
2. The package is extracted (as a ZIP file)
3. The `.nuspec` file is parsed for validation only
4. Only DLLs found directly in the extracted package are referenced
5. **Dependencies declared in `.nuspec` are NOT downloaded or referenced**

### Example .nuspec with Dependencies

```xml
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>BlazorDataOrchestrator.Job</id>
    <version>1.0.20260120064118</version>
    <dependencies>
      <group targetFramework="net10.0">
        <dependency id="Azure.Storage.Blobs" version="12.26.0" />
        <dependency id="SendGrid" version="9.29.3" />
        <dependency id="Microsoft.EntityFrameworkCore" version="10.0.0" />
        <dependency id="Microsoft.EntityFrameworkCore.SqlServer" version="10.0.0" />
        <dependency id="Azure.Data.Tables" version="12.9.1" />
      </group>
    </dependencies>
  </metadata>
</package>
```

---

## Solution Overview

We need to implement a NuGet dependency resolution mechanism that:

1. **Parses the `.nuspec` file** to extract all dependencies
2. **Downloads the dependent packages** from NuGet.org (or configured package sources)
3. **Extracts and locates the correct DLLs** for the target framework
4. **References all required assemblies** in the CS-Script evaluator before compilation

### Solution Options

| Option | Approach | Pros | Cons |
|--------|----------|------|------|
| **A** | Use `dotnet restore` with a generated `.csproj` | Handles transitive dependencies, official tooling | Requires .NET SDK on agent, slower |
| **B** | Direct NuGet API calls (NuGet.Protocol) | Fine-grained control, no SDK required | Must handle transitive deps manually |
| **C** | Cache pre-resolved dependencies | Fastest execution | Complex cache management |
| **D** | Hybrid: `dotnet restore` with caching | Best of both worlds | More complex implementation |

**Recommended: Option D (Hybrid approach with dotnet restore and caching)**

---

## Implementation Plan

### Phase 1: Parse Dependencies from .nuspec

#### 1.1 Create NuGet Dependency Model
**File:** `src/BlazorDataOrchestrator.Core/Models/NuGetDependency.cs` (New)

```csharp
public class NuGetDependency
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? TargetFramework { get; set; }
}

public class NuGetDependencyGroup
{
    public string? TargetFramework { get; set; }
    public List<NuGetDependency> Dependencies { get; set; } = new();
}
```

#### 1.2 Update PackageProcessorService
**File:** `src/BlazorDataOrchestrator.Core/Services/PackageProcessorService.cs`

Add method to parse dependencies from `.nuspec`:

```csharp
public async Task<List<NuGetDependencyGroup>> GetDependenciesFromNuSpecAsync(string extractedPath)
{
    // Parse .nuspec XML
    // Extract <dependencies> element
    // Return structured dependency groups
}
```

---

### Phase 2: NuGet Package Resolution Service

#### 2.1 Create NuGetResolverService
**File:** `src/BlazorDataOrchestrator.Core/Services/NuGetResolverService.cs` (New)

**Purpose:** Resolve and download NuGet packages and their transitive dependencies

**Key Methods:**
- `ResolveAndDownloadAsync(List<NuGetDependency> dependencies, string targetFramework, string outputPath)`
- `GetAssemblyPathsAsync(string packagePath, string targetFramework)`

**Implementation Strategy:**

```csharp
public class NuGetResolverService
{
    private readonly string _nugetCachePath;
    private readonly ILogger<NuGetResolverService> _logger;

    public async Task<List<string>> ResolveAndDownloadAsync(
        List<NuGetDependency> dependencies, 
        string targetFramework,
        string workingDirectory)
    {
        // 1. Generate temporary .csproj with PackageReferences
        // 2. Run `dotnet restore`
        // 3. Parse obj/project.assets.json for resolved assemblies
        // 4. Return list of assembly paths
    }
}
```

#### 2.2 Generate Temporary Project File

Create a minimal `.csproj` for dependency resolution:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SendGrid" Version="9.29.3" />
    <PackageReference Include="Azure.Storage.Blobs" Version="12.26.0" />
    <!-- ... other dependencies -->
  </ItemGroup>
</Project>
```

#### 2.3 Parse project.assets.json

After `dotnet restore`, parse the generated `obj/project.assets.json` to get:
- All resolved package versions (including transitive dependencies)
- Assembly paths for each package
- Runtime assembly information

---

### Phase 3: Update CodeExecutorService

#### 3.1 Integrate Dependency Resolution
**File:** `src/BlazorDataOrchestrator.Core/Services/CodeExecutorService.cs`

Update `ExecuteCSharpAsync` to:

1. Call `PackageProcessorService.GetDependenciesFromNuSpecAsync()` to get dependencies
2. Call `NuGetResolverService.ResolveAndDownloadAsync()` to resolve packages
3. Add all resolved assembly paths to the CS-Script evaluator

```csharp
private async Task<CodeExecutionResult> ExecuteCSharpAsync(string extractedPath, JobExecutionContext context)
{
    // ... existing code ...
    
    // NEW: Resolve NuGet dependencies
    var dependencies = await _packageProcessor.GetDependenciesFromNuSpecAsync(extractedPath);
    if (dependencies.Any())
    {
        result.Logs.Add($"Resolving {dependencies.Count} NuGet dependencies...");
        var resolvedAssemblies = await _nugetResolver.ResolveAndDownloadAsync(
            dependencies, 
            "net10.0",  // or detect from nuspec
            extractedPath);
        
        foreach (var assembly in resolvedAssemblies)
        {
            try
            {
                evaluator.ReferenceAssembly(assembly);
                result.Logs.Add($"Added dependency: {Path.GetFileName(assembly)}");
            }
            catch (Exception ex)
            {
                result.Logs.Add($"Warning: Could not load {Path.GetFileName(assembly)}: {ex.Message}");
            }
        }
    }
    
    // ... rest of existing code ...
}
```

---

### Phase 4: Caching Strategy

#### 4.1 Package Cache Location

Use a persistent cache directory to avoid re-downloading packages:

```
%LOCALAPPDATA%\BlazorDataOrchestrator\NuGetCache\
  └── packages\
      ├── sendgrid\9.29.3\
      │   └── lib\net6.0\SendGrid.dll
      ├── azure.storage.blobs\12.26.0\
      │   └── lib\net6.0\Azure.Storage.Blobs.dll
      └── ...
```

#### 4.2 Cache Key Strategy

- Key: `{PackageId}_{Version}_{TargetFramework}`
- Store resolved assembly paths in a manifest file
- Check cache before calling `dotnet restore`

#### 4.3 Cache Service
**File:** `src/BlazorDataOrchestrator.Core/Services/NuGetCacheService.cs` (New)

```csharp
public class NuGetCacheService
{
    public bool IsCached(string packageId, string version, string targetFramework);
    public List<string> GetCachedAssemblyPaths(string packageId, string version, string targetFramework);
    public void CachePackage(string packageId, string version, string targetFramework, List<string> assemblies);
}
```

---

### Phase 5: Error Handling and Logging

#### 5.1 Handle Resolution Failures

- Log detailed error messages when packages cannot be resolved
- Fall back to partial execution if non-critical packages fail
- Provide actionable error messages to users

#### 5.2 Diagnostic Logging

Add verbose logging for troubleshooting:
- Package resolution steps
- Assembly loading success/failure
- Cache hits/misses
- `dotnet restore` output

---

## File Changes Summary

| File | Action | Description |
|------|--------|-------------|
| `Models/NuGetDependency.cs` | Create | Dependency model classes |
| `Services/NuGetResolverService.cs` | Create | Main resolution service |
| `Services/NuGetCacheService.cs` | Create | Package caching service |
| `Services/PackageProcessorService.cs` | Modify | Add dependency parsing |
| `Services/CodeExecutorService.cs` | Modify | Integrate dependency resolution |
| `BlazorDataOrchestrator.Core.csproj` | Modify | Add NuGet.Protocol package reference |

---

## Dependencies to Add

```xml
<!-- In BlazorDataOrchestrator.Core.csproj -->
<PackageReference Include="NuGet.Protocol" Version="6.12.2" />
```

---

## Testing Strategy

### Unit Tests
- `NuGetResolverService` dependency parsing
- Cache hit/miss scenarios
- Target framework selection

### Integration Tests
- Full job execution with nested dependencies
- Multiple dependency groups (different target frameworks)
- Package version conflict resolution

### Test Package
Use the provided `NestedNuGetPackageTest.zip` for end-to-end testing.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| .NET SDK not installed on Agent | High | Document requirement, check on startup |
| Network failures during restore | Medium | Implement retry logic, use cache |
| Package version conflicts | Medium | Let `dotnet restore` handle resolution |
| Large transitive dependency trees | Low | Cache aggressively, async processing |
| Framework version mismatches | Medium | Parse target framework from .nuspec |

---

## Alternative: Pre-Package Dependencies (Optional Enhancement)

For production scenarios, consider adding an option to **bundle all dependencies** into the Job package at creation time:

1. When creating a Job package in Job Creator, resolve all dependencies
2. Include all DLLs in the `.nupkg` file
3. This makes the Agent simpler (no `dotnet restore` needed)

This could be an opt-in feature for users who want faster, more reliable execution.

---

## Implementation Order

1. **Phase 1:** Parse dependencies from .nuspec (1-2 hours)
2. **Phase 2:** Create NuGetResolverService with `dotnet restore` (4-6 hours)
3. **Phase 3:** Update CodeExecutorService integration (2-3 hours)
4. **Phase 4:** Implement caching (2-3 hours)
5. **Phase 5:** Error handling and logging (1-2 hours)
6. **Testing:** End-to-end testing with sample packages (2-3 hours)

**Estimated Total:** 12-19 hours

---

## References

- [CS-Script Wiki - Hosted Script Execution](https://github.com/oleg-shilo/cs-script/wiki/Hosted-Script-Execution)
- [CS-Script Wiki - NuGet Support](https://github.com/oleg-shilo/cs-script/wiki/NuGet-Support)
- [NuGet.Protocol Documentation](https://docs.microsoft.com/en-us/nuget/reference/nuget-client-sdk)
- [project.assets.json Format](https://docs.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)

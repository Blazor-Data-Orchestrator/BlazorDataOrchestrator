# Job Template Zip Automation Plan

## Problem

The `BlazorDataOrchestrator.JobCreatorTemplate` project is shipped to users as a zip file located at `src/BlazorOrchestrator.Web/JobTemplate/BlazorDataOrchestrator.JobCreatorTemplate.zip`. Today this zip is created **manually** with error-prone steps:

1. Copy the entire `src/BlazorDataOrchestrator.JobCreatorTemplate` directory
2. Delete `bin/`, `obj/`, `Properties/` directories
3. Delete `BlazorDataOrchestrator.JobCreatorTemplate.csproj.user`
4. Include the root-level `JobTemplate.slnx` file
5. Zip everything and place it in `src/BlazorOrchestrator.Web/JobTemplate/`

This is tedious, easy to forget, and can lead to stale zip files being deployed.

---

## Goals

- **Zero manual steps** — the zip is regenerated automatically on every build of `BlazorOrchestrator.Web`.
- **Always in sync** — any change to the template project is immediately reflected in the zip.
- **No extra tools** — use only MSBuild targets and built-in .NET SDK capabilities (or a small PowerShell/shell script called from MSBuild).
- **CI-friendly** — works in both local dev (`dotnet build` / `aspire run`) and CI/CD pipelines.

---

## Proposed Solution

### Option A — MSBuild Target in `BlazorOrchestrator.Web.csproj` (Recommended)

Add a `<Target>` to `BlazorOrchestrator.Web.csproj` that runs **before** the build, invokes a PowerShell/shell script (or inline MSBuild tasks) to produce the zip, and places it in the `JobTemplate/` folder so it gets included as content.

#### Implementation Steps

##### 1. Create the packaging script

Create `scripts/Package-JobTemplate.ps1`:

```powershell
<#
.SYNOPSIS
    Packages the JobCreatorTemplate project into a zip for distribution.
#>
param(
    [Parameter(Mandatory)]
    [string]$SourceDir,          # Path to src/BlazorDataOrchestrator.JobCreatorTemplate

    [Parameter(Mandatory)]
    [string]$SlnxFile,           # Path to JobTemplate.slnx

    [Parameter(Mandatory)]
    [string]$OutputZip           # Full path for the output .zip file
)

$ErrorActionPreference = 'Stop'

# Create a temp staging directory
$stagingDir = Join-Path ([System.IO.Path]::GetTempPath()) "JobTemplateStaging_$([guid]::NewGuid().ToString('N'))"
$templateDir = Join-Path $stagingDir "BlazorDataOrchestrator.JobCreatorTemplate"

try {
    # Copy the template project to staging
    Copy-Item -Path $SourceDir -Destination $templateDir -Recurse

    # Remove directories that should not be shipped
    $dirsToRemove = @('bin', 'obj', 'Properties')
    foreach ($dir in $dirsToRemove) {
        $target = Join-Path $templateDir $dir
        if (Test-Path $target) {
            Remove-Item $target -Recurse -Force
        }
    }

    # Remove user-specific files
    $filesToRemove = @(
        '*.csproj.user',
        'execution_errors.log'
    )
    foreach ($pattern in $filesToRemove) {
        Get-ChildItem -Path $templateDir -Filter $pattern -Recurse | Remove-Item -Force
    }

    # Copy the .slnx file into the staging root (sibling to the project folder)
    Copy-Item -Path $SlnxFile -Destination $stagingDir

    # Ensure the output directory exists
    $outputDir = Split-Path $OutputZip -Parent
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    # Remove old zip if it exists
    if (Test-Path $OutputZip) {
        Remove-Item $OutputZip -Force
    }

    # Create the zip
    Compress-Archive -Path "$stagingDir\*" -DestinationPath $OutputZip -Force

    Write-Host "Created template zip: $OutputZip"
}
finally {
    # Clean up staging
    if (Test-Path $stagingDir) {
        Remove-Item $stagingDir -Recurse -Force
    }
}
```

##### 2. Add MSBuild target to `BlazorOrchestrator.Web.csproj`

```xml
<!-- Auto-package the JobCreatorTemplate zip before build -->
<Target Name="PackageJobTemplate" BeforeTargets="BeforeBuild"
        Inputs="$(MSBuildThisFileDirectory)..\BlazorDataOrchestrator.JobCreatorTemplate\**\*.*"
        Outputs="$(MSBuildThisFileDirectory)JobTemplate\BlazorDataOrchestrator.JobCreatorTemplate.zip">
  <Exec Command="pwsh -NoProfile -ExecutionPolicy Bypass -File &quot;$(MSBuildThisFileDirectory)..\..\scripts\Package-JobTemplate.ps1&quot; -SourceDir &quot;$(MSBuildThisFileDirectory)..\BlazorDataOrchestrator.JobCreatorTemplate&quot; -SlnxFile &quot;$(MSBuildThisFileDirectory)..\..\JobTemplate.slnx&quot; -OutputZip &quot;$(MSBuildThisFileDirectory)JobTemplate\BlazorDataOrchestrator.JobCreatorTemplate.zip&quot;" />
</Target>
```

Key details:
- **`Inputs` / `Outputs`** — MSBuild incremental build support. The target only re-runs when template source files are newer than the zip, avoiding unnecessary work on every build.
- **`BeforeTargets="BeforeBuild"`** — ensures the zip is ready before the Web project compiles and copies content to output.

##### 3. Ensure the zip is included as content

Verify that `BlazorOrchestrator.Web.csproj` already includes the zip as content (it likely does via a glob). If not, add:

```xml
<ItemGroup>
  <Content Include="JobTemplate\BlazorDataOrchestrator.JobCreatorTemplate.zip">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

##### 4. Add the zip to `.gitignore`

Since the zip is now a build artifact, it should not be committed to source control:

```
# Auto-generated job template zip
src/BlazorOrchestrator.Web/JobTemplate/BlazorDataOrchestrator.JobCreatorTemplate.zip
```

---

### Option B — Standalone Script Only (Simpler, Less Integrated)

If MSBuild integration is not desired, the same `Package-JobTemplate.ps1` script can be run manually or as a CI step. This is simpler but requires developers to remember to run it.

Usage:
```powershell
.\scripts\Package-JobTemplate.ps1 `
    -SourceDir "src\BlazorDataOrchestrator.JobCreatorTemplate" `
    -SlnxFile "JobTemplate.slnx" `
    -OutputZip "src\BlazorOrchestrator.Web\JobTemplate\BlazorDataOrchestrator.JobCreatorTemplate.zip"
```

---

### Option C — CI-Only Automation

Add a step to the CI/CD pipeline (GitHub Actions / Azure DevOps) that runs the script before building the Web project. The zip remains committed in the repo for local dev but is always regenerated in CI.

---

## Recommendation

**Option A** is recommended because:
- It is fully automatic — no manual steps, no forgotten updates.
- Incremental builds prevent performance impact (the zip is only regenerated when template files change).
- Works identically in local dev and CI.
- The script is cross-platform compatible (PowerShell 7 / `pwsh` runs on Windows, Linux, macOS).

---

## Files Changed

| File | Action | Purpose |
|------|--------|---------|
| `scripts/Package-JobTemplate.ps1` | **Create** | Packaging script |
| `src/BlazorOrchestrator.Web/BlazorOrchestrator.Web.csproj` | **Edit** | Add `PackageJobTemplate` MSBuild target |
| `.gitignore` | **Edit** | Exclude the generated zip |

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| `pwsh` not installed on build agent | .NET 10 SDK images include PowerShell 7. Add a check/fallback in CI. Alternatively, rewrite the script as a cross-platform shell script or pure MSBuild tasks. |
| Incremental build `Inputs` glob is too broad | Refine the glob or add `Condition` checks if build times are affected. |
| Zip contents diverge from expectations | Add a smoke test that extracts the zip and verifies expected files are present (e.g., `.csproj`, `Program.cs`, `.slnx`, no `bin/`). |

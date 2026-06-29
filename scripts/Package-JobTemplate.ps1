<#
.SYNOPSIS
    Packages the JobCreatorTemplate project into a zip for distribution.
.DESCRIPTION
    Copies the JobCreatorTemplate source, removes build artifacts and user-specific files,
    includes the JobCreatorTemplate.slnx (from the .slnx.template), and produces a zip ready for the Web project.
.PARAMETER SourceDir
    Path to the BlazorDataOrchestrator.JobCreatorTemplate project directory.
.PARAMETER OutputZip
    Full path for the output .zip file.
#>
param(
    [Parameter(Mandatory)]
    [string]$SourceDir,

    [Parameter(Mandatory)]
    [string]$OutputZip
)

$ErrorActionPreference = 'Stop'

# Create a temp staging directory
$stagingDir = Join-Path ([System.IO.Path]::GetTempPath()) "JobTemplateStaging_$([guid]::NewGuid().ToString('N'))"
$templateDir = Join-Path $stagingDir "BlazorDataOrchestrator.JobCreatorTemplate"

try {
    Write-Host "Packaging JobCreatorTemplate..."

    # Copy the template project to staging, excluding directories that should not be
    # shipped. robocopy is used (instead of Copy-Item) because bin/obj can contain
    # deeply nested node packages whose paths exceed Windows MAX_PATH, which makes a
    # naive recursive copy fail with DirectoryNotFoundException.
    New-Item -ItemType Directory -Path $templateDir -Force | Out-Null
    $dirsToRemove = @('bin', 'obj', 'Properties')
    robocopy $SourceDir $templateDir /E /XD $dirsToRemove /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed copying template (exit code $LASTEXITCODE)"
    }

    # Remove __pycache__ directories anywhere in the tree
    Get-ChildItem -Path $templateDir -Directory -Filter '__pycache__' -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object {
            Remove-Item $_.FullName -Recurse -Force
            Write-Host "  Removed $($_.FullName | Split-Path -Leaf)/ (pycache)"
        }

    # Remove user-specific and transient files
    $filesToRemove = @(
        '*.csproj.user',
        'execution_errors.log'
    )
    foreach ($pattern in $filesToRemove) {
        Get-ChildItem -Path $templateDir -Filter $pattern -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object {
                Remove-Item $_.FullName -Force
                Write-Host "  Removed $($_.Name)"
            }
    }

    # Patch the ProjectReference in the staged .csproj so it resolves correctly
    # In the extracted layout the project is one level deeper (inside a subfolder of the output dir),
    # so the relative path to BlazorDataOrchestrator.Core needs an extra "../"
    $stagedCsproj = Get-ChildItem -Path $templateDir -Filter '*.csproj' -Recurse | Select-Object -First 1
    if ($stagedCsproj) {
        $csprojContent = Get-Content $stagedCsproj.FullName -Raw
        $csprojContent = $csprojContent -replace 'Include="\.\.\\BlazorDataOrchestrator\.Core\\', 'Include="..\..\BlazorDataOrchestrator.Core\'
        $csprojContent = $csprojContent -replace 'Include="\.\./BlazorDataOrchestrator\.Core/', 'Include="../../BlazorDataOrchestrator.Core/'
        Set-Content -Path $stagedCsproj.FullName -Value $csprojContent -NoNewline
        Write-Host "  Patched ProjectReference path in $($stagedCsproj.Name)"
    }

    # Copy the .slnx template into the staging root (sibling to the project folder),
    # renaming it from JobCreatorTemplate.slnx.template to JobCreatorTemplate.slnx
    $slnxTemplate = Join-Path $PSScriptRoot '..\src\BlazorOrchestrator.Web\JobTemplate\JobCreatorTemplate.slnx.template'
    $slnxDest = Join-Path $stagingDir 'JobCreatorTemplate.slnx'
    Copy-Item -Path $slnxTemplate -Destination $slnxDest
    Write-Host "  Included JobCreatorTemplate.slnx"

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

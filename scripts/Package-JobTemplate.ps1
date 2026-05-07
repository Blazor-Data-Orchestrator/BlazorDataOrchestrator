<#
.SYNOPSIS
    Packages the JobCreatorTemplate project into a zip for distribution.
.DESCRIPTION
    Copies the JobCreatorTemplate source, removes build artifacts and user-specific files,
    includes the JobTemplate.slnx, and produces a zip ready for the Web project.
.PARAMETER SourceDir
    Path to the BlazorDataOrchestrator.JobCreatorTemplate project directory.
.PARAMETER SlnxFile
    Path to the JobTemplate.slnx file.
.PARAMETER OutputZip
    Full path for the output .zip file.
#>
param(
    [Parameter(Mandatory)]
    [string]$SourceDir,

    [Parameter(Mandatory)]
    [string]$SlnxFile,

    [Parameter(Mandatory)]
    [string]$OutputZip
)

$ErrorActionPreference = 'Stop'

# Create a temp staging directory
$stagingDir = Join-Path ([System.IO.Path]::GetTempPath()) "JobTemplateStaging_$([guid]::NewGuid().ToString('N'))"
$templateDir = Join-Path $stagingDir "BlazorDataOrchestrator.JobCreatorTemplate"

try {
    Write-Host "Packaging JobCreatorTemplate..."

    # Copy the template project to staging
    Copy-Item -Path $SourceDir -Destination $templateDir -Recurse

    # Remove directories that should not be shipped
    $dirsToRemove = @('bin', 'obj', 'Properties')
    foreach ($dir in $dirsToRemove) {
        $target = Join-Path $templateDir $dir
        if (Test-Path $target) {
            Remove-Item $target -Recurse -Force
            Write-Host "  Removed $dir/"
        }
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

    # Copy the .slnx file into the staging root (sibling to the project folder)
    Copy-Item -Path $SlnxFile -Destination $stagingDir
    Write-Host "  Included $(Split-Path $SlnxFile -Leaf)"

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

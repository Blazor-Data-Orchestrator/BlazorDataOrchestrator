# Shared helpers for Check-Version.ps1 and New-DeployManifest.ps1 (dot-source this file).
# Works in Windows PowerShell 5.1 and PowerShell 7.

function Get-RepositoryRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

# v1.0.0 -> 01.00.00 (each part 0-99, no pre-release suffix)
function ConvertFrom-ReleaseTag {
    param([Parameter(Mandatory)] [string] $Tag)

    if ($Tag -notmatch '^v(\d{1,2})\.(\d{1,2})\.(\d{1,2})$') {
        throw "'$Tag' is not a release tag in the form v<major>.<minor>.<patch> (each part 0-99)."
    }
    return '{0:00}.{1:00}.{2:00}' -f [int]$Matches[1], [int]$Matches[2], [int]$Matches[3]
}

# v1.0.0 -> 1.0.0 (the container image tag)
function Get-SemVerFromTag {
    param([Parameter(Mandatory)] [string] $Tag)
    return $Tag.TrimStart('v')
}

function Get-ApplicationVersion {
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $path = Join-Path $RepoRoot 'src/BlazorDataOrchestrator.Core/ApplicationVersion.cs'
    $match = Select-String -Path $path -Pattern 'const\s+string\s+Current\s*=\s*"([^"]+)"' | Select-Object -First 1
    if (-not $match) { throw "ApplicationVersion.Current not found in $path." }
    return $match.Matches[0].Groups[1].Value
}

function Get-SqlVersionFile {
    param([Parameter(Mandatory)] [string] $RepoRoot)
    return (Get-Content -Path (Join-Path $RepoRoot 'SQLVersion') -Raw).Trim()
}

function Get-NewestSqlScriptVersion {
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $folder = Join-Path $RepoRoot 'src/BlazorOrchestrator.Web/!SQL'
    $newest = Get-ChildItem -LiteralPath $folder -Filter '*.sql' -File |
        Sort-Object -Property Name |
        Select-Object -Last 1
    if (-not $newest) { throw "No .sql scripts found in $folder." }
    return [IO.Path]::GetFileNameWithoutExtension($newest.Name)
}

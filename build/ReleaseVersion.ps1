# Shared helpers for Check-Version.ps1 and New-DeployManifest.ps1 (dot-source this file).
# Works in Windows PowerShell 5.1 and PowerShell 7.
#
# Version rules (the same rules VersionConsistencyTests enforces):
#   - The release tag v<major>.<minor>.<patch> maps to ApplicationVersion.Current NN.NN.NN
#     (v1.60.0 -> 01.60.00).
#   - The root SQLVersion file equals ApplicationVersion.Current.
#   - Every !SQL script is named NN.NN.NN.sql and none is newer than ApplicationVersion.Current.
#     A release without schema changes has no script of its own, so the newest script may be older.

function Get-RepositoryRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

# v1.60.0 -> 01.60.00 (each part 0-99, no pre-release suffix)
function ConvertFrom-ReleaseTag {
    param([Parameter(Mandatory)] [string] $Tag)

    if ($Tag -notmatch '^v(\d{1,2})\.(\d{1,2})\.(\d{1,2})$') {
        throw "'$Tag' is not a release tag in the form v<major>.<minor>.<patch> (each part 0-99)."
    }
    return '{0:00}.{1:00}.{2:00}' -f [int]$Matches[1], [int]$Matches[2], [int]$Matches[3]
}

# v1.60.0 -> 1.60.0 (the container image tag)
function Get-ImageTagFromReleaseTag {
    param([Parameter(Mandatory)] [string] $Tag)
    return $Tag.TrimStart('v')
}

# 01.60.00 -> 16000, for comparisons
function ConvertTo-VersionNumber {
    param([Parameter(Mandatory)] [string] $Version)

    if ($Version -notmatch '^(\d{2})\.(\d{2})\.(\d{2})$') {
        throw "'$Version' is not a version in the form NN.NN.NN."
    }
    return [int]$Matches[1] * 10000 + [int]$Matches[2] * 100 + [int]$Matches[3]
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

function Get-SqlScriptVersions {
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $folder = Join-Path $RepoRoot 'src/BlazorOrchestrator.Web/!SQL'
    $scripts = @(Get-ChildItem -LiteralPath $folder -Filter '*.sql' -File)
    if ($scripts.Count -eq 0) { throw "No .sql scripts found in $folder." }

    foreach ($script in $scripts) {
        if ($script.Name -notmatch '^\d{2}\.\d{2}\.\d{2}\.sql$') {
            throw "!SQL/$($script.Name) is not named NN.NN.NN.sql."
        }
    }
    return @($scripts | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) } | Sort-Object { ConvertTo-VersionNumber $_ })
}

<#
.SYNOPSIS
    Fails when a release tag does not match the application version.

.DESCRIPTION
    The release tag (v1.60.0) must equal ApplicationVersion.Current (01.60.00) and the root
    SQLVersion file, and no !SQL script may be newer than that version (a release without schema
    changes has no script of its own). release.yml runs this before any image is pushed.

.PARAMETER Tag
    The release tag, for example v1.60.0. Defaults to GITHUB_REF_NAME.

.EXAMPLE
    ./build/Check-Version.ps1 -Tag v1.60.0
#>
[CmdletBinding()]
param(
    [string] $Tag = $env:GITHUB_REF_NAME
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')

if ([string]::IsNullOrWhiteSpace($Tag)) {
    throw 'No release tag. Pass -Tag v<major>.<minor>.<patch> or set GITHUB_REF_NAME.'
}

$repoRoot = Get-RepositoryRoot
$tagVersion = ConvertFrom-ReleaseTag -Tag $Tag
$applicationVersion = Get-ApplicationVersion -RepoRoot $repoRoot
$sqlVersion = Get-SqlVersionFile -RepoRoot $repoRoot
$scripts = Get-SqlScriptVersions -RepoRoot $repoRoot
$newestScript = $scripts[-1]

Write-Host ("{0,-40} {1}" -f "Release tag ($Tag)", $tagVersion)
Write-Host ("{0,-40} {1}" -f 'ApplicationVersion.Current', $applicationVersion)
Write-Host ("{0,-40} {1}" -f 'SQLVersion file', $SqlVersion)
Write-Host ("{0,-40} {1}" -f 'Newest !SQL script', "$newestScript.sql")

$errors = @()
if ($applicationVersion -ne $tagVersion) {
    $errors += "ApplicationVersion.Current is $applicationVersion, but tag $Tag means $tagVersion."
}
if ($sqlVersion -ne $tagVersion) {
    $errors += "SQLVersion is $sqlVersion, but tag $Tag means $tagVersion."
}
if ((ConvertTo-VersionNumber $newestScript) -gt (ConvertTo-VersionNumber $tagVersion)) {
    $errors += "!SQL/$newestScript.sql is newer than $tagVersion."
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "ERROR: $_" }
    throw "Version mismatch. Update ApplicationVersion.Current and SQLVersion to $tagVersion before tagging $Tag."
}

Write-Host "Version check passed: $Tag = $tagVersion"

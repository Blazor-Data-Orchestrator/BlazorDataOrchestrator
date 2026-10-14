<#
.SYNOPSIS
    Fails when a release tag does not match the application version.

.DESCRIPTION
    The release tag (v1.0.0), ApplicationVersion.Current (01.00.00), the root SQLVersion file and
    the newest !SQL script name must all describe the same version. release.yml runs this before
    any image is pushed.

.PARAMETER Tag
    The release tag, for example v1.0.0. Defaults to GITHUB_REF_NAME.

.EXAMPLE
    ./build/Check-Version.ps1 -Tag v1.0.0
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
$versions = [ordered]@{
    "Release tag ($Tag)"                                = $tagVersion
    'ApplicationVersion.Current'                         = Get-ApplicationVersion -RepoRoot $repoRoot
    'SQLVersion file'                                    = Get-SqlVersionFile -RepoRoot $repoRoot
    'Newest src/BlazorOrchestrator.Web/!SQL script'      = Get-NewestSqlScriptVersion -RepoRoot $repoRoot
}

$versions.GetEnumerator() | ForEach-Object { Write-Host ("{0,-50} {1}" -f $_.Key, $_.Value) }

$distinct = @($versions.Values | Select-Object -Unique)
if ($distinct.Count -ne 1) {
    throw "Version mismatch. Update ApplicationVersion.Current, SQLVersion and the !SQL scripts so they all equal $tagVersion before tagging $Tag."
}

Write-Host "Version check passed: $Tag = $tagVersion"

<#
.SYNOPSIS
    Writes deploy-manifest.json for a BlazorDataOrchestrator release.

.DESCRIPTION
    deploy-manifest.json is the machine-readable contract BlazorData.net reads from each GitHub
    Release (schema: BlazorData-Deployment-Service-Plan.md section 8.3). It records:

      tooling.dotnetSdk          global.json sdk.version
      tooling.aspireCli          the AppHost's Aspire.AppHost.Sdk version
      tooling.azd / engine       build/tooling.json
      apphostPath                aspire.config.json appHost.path (folder, relative to the repo root)
      sourceSha256               SHA256 of the release source zipball
      services.*.image/digest    the GHCR images pushed by release.yml and their digests
      parameters                 the optional AppHost parameters (Setting("...") calls in Program.cs)
      minUpgradeFrom /
      requiresTopologyChange     build/release-settings.json

.PARAMETER Tag
    The release tag, for example v1.0.0. Defaults to GITHUB_REF_NAME.

.PARAMETER Out
    Path of the manifest to write.

.PARAMETER WebDigest
    Digest (sha256:<64 hex>) of ghcr.io/blazor-data-orchestrator/bdo-web:<version>.

.PARAMETER SchedulerDigest
    Digest of ghcr.io/blazor-data-orchestrator/bdo-scheduler:<version>.

.PARAMETER AgentDigest
    Digest of ghcr.io/blazor-data-orchestrator/bdo-agent:<version>.

.PARAMETER SourceArchive
    Local copy of the release zipball. When omitted, the zipball is downloaded from
    https://github.com/<Repository>/archive/refs/tags/<Tag>.zip (the tag must already be pushed).

.PARAMETER Repository
    owner/name of the GitHub repository. Defaults to GITHUB_REPOSITORY.

.EXAMPLE
    ./build/New-DeployManifest.ps1 -Tag v1.0.0 -Out out/deploy-manifest.json `
        -WebDigest sha256:... -SchedulerDigest sha256:... -AgentDigest sha256:...
#>
[CmdletBinding()]
param(
    [string] $Tag = $env:GITHUB_REF_NAME,
    [Parameter(Mandatory)] [string] $Out,
    [string] $WebDigest = $env:WEB_DIGEST,
    [string] $SchedulerDigest = $env:SCHEDULER_DIGEST,
    [string] $AgentDigest = $env:AGENT_DIGEST,
    [string] $SourceArchive,
    [string] $Repository = $(if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'Blazor-Data-Orchestrator/BlazorDataOrchestrator' })
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')

# BlazorData.net only accepts images from this registry namespace.
$ImagePrefix = 'ghcr.io/blazor-data-orchestrator'

function Read-Json([string] $Path) {
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Assert-Digest([string] $Name, [string] $Value) {
    if ($Value -cnotmatch '^sha256:[0-9a-f]{64}$') {
        throw "$Name digest '$Value' is not in the form sha256:<64 lowercase hex characters>."
    }
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
    throw 'No release tag. Pass -Tag v<major>.<minor>.<patch> or set GITHUB_REF_NAME.'
}

$repoRoot = Get-RepositoryRoot

# Version: the tag must match the code (Check-Version.ps1 checks SQLVersion and the scripts too).
$appVersion = ConvertFrom-ReleaseTag -Tag $Tag
$codeVersion = Get-ApplicationVersion -RepoRoot $repoRoot
if ($codeVersion -ne $appVersion) {
    throw "Release tag $Tag ($appVersion) does not match ApplicationVersion.Current ($codeVersion)."
}
$imageTag = Get-SemVerFromTag -Tag $Tag

# Tooling
$globalJson = Read-Json (Join-Path $repoRoot 'global.json')
$dotnetSdk = $globalJson.sdk.version
if ([string]::IsNullOrWhiteSpace($dotnetSdk)) { throw 'global.json has no sdk.version.' }

$aspireConfig = Read-Json (Join-Path $repoRoot 'aspire.config.json')
$appHostProject = $aspireConfig.appHost.path
if ([string]::IsNullOrWhiteSpace($appHostProject)) { throw 'aspire.config.json has no appHost.path.' }
$apphostPath = (Split-Path -Path $appHostProject -Parent) -replace '\\', '/'

[xml] $appHostCsproj = Get-Content -LiteralPath (Join-Path $repoRoot $appHostProject) -Raw
$sdkAttribute = $appHostCsproj.Project.Sdk
if ($sdkAttribute -notmatch '^Aspire\.AppHost\.Sdk/(.+)$') {
    throw "The AppHost project Sdk attribute '$sdkAttribute' is not Aspire.AppHost.Sdk/<version>."
}
$aspireCli = $Matches[1]

$tooling = Read-Json (Join-Path $PSScriptRoot 'tooling.json')
$releaseSettings = Read-Json (Join-Path $PSScriptRoot 'release-settings.json')
if ($null -eq $releaseSettings.requiresTopologyChange -or [string]::IsNullOrWhiteSpace($releaseSettings.minUpgradeFrom)) {
    throw 'build/release-settings.json must set requiresTopologyChange and minUpgradeFrom.'
}
if ($releaseSettings.minUpgradeFrom -notmatch '^\d{2}\.\d{2}\.\d{2}$') {
    throw "minUpgradeFrom '$($releaseSettings.minUpgradeFrom)' is not in the form 01.00.00."
}

# AppHost parameters: every Setting("name") / UseExistingWhenSet(x, "name", "resourceGroup") in Program.cs
$appHostSource = Get-Content -LiteralPath (Join-Path $repoRoot (Join-Path $apphostPath 'Program.cs')) -Raw
$parameterNames = New-Object System.Collections.Generic.List[string]
$pattern = '(?:Setting|Parameter)\("(?<name>[A-Za-z][A-Za-z0-9]*)"\)|UseExistingWhenSet\(\s*\w+\s*,\s*"(?<name>[A-Za-z][A-Za-z0-9]*)"\s*,\s*"(?<rg>[A-Za-z][A-Za-z0-9]*)"\s*\)'
foreach ($match in [regex]::Matches($appHostSource, $pattern)) {
    foreach ($group in @('name', 'rg')) {
        $value = $match.Groups[$group].Value
        if ($value -and -not $parameterNames.Contains($value)) { $parameterNames.Add($value) }
    }
}
if ($parameterNames.Count -eq 0) { throw 'No AppHost parameters found in Program.cs.' }
$parameters = @($parameterNames | ForEach-Object {
    [ordered]@{ name = $_; type = 'string'; optional = $true }
})

# Images and digests
Assert-Digest 'bdo-web' $WebDigest
Assert-Digest 'bdo-scheduler' $SchedulerDigest
Assert-Digest 'bdo-agent' $AgentDigest

# Source zipball hash
if (-not $SourceArchive) {
    $SourceArchive = Join-Path ([IO.Path]::GetTempPath()) "bdo-source-$Tag.zip"
    $zipUrl = "https://github.com/$Repository/archive/refs/tags/$Tag.zip"
    Write-Host "Downloading $zipUrl"
    Invoke-WebRequest -Uri $zipUrl -OutFile $SourceArchive -UseBasicParsing
}
$sourceSha256 = (Get-FileHash -LiteralPath $SourceArchive -Algorithm SHA256).Hash.ToLowerInvariant()

$manifest = [ordered]@{
    schemaVersion          = 1
    appVersion             = $appVersion
    releaseTag             = $Tag
    tooling                = [ordered]@{
        dotnetSdk = $dotnetSdk
        aspireCli = $aspireCli
        azd       = $tooling.azd
        engine    = $tooling.engine
    }
    apphostPath            = $apphostPath
    sourceSha256           = $sourceSha256
    services               = [ordered]@{
        webapp    = [ordered]@{ image = "$ImagePrefix/bdo-web:$imageTag"; digest = $WebDigest }
        scheduler = [ordered]@{ image = "$ImagePrefix/bdo-scheduler:$imageTag"; digest = $SchedulerDigest }
        agent     = [ordered]@{ image = "$ImagePrefix/bdo-agent:$imageTag"; digest = $AgentDigest }
    }
    parameters             = $parameters
    minUpgradeFrom         = $releaseSettings.minUpgradeFrom
    requiresTopologyChange = [bool] $releaseSettings.requiresTopologyChange
    releaseNotesUrl        = "https://github.com/$Repository/releases/tag/$Tag"
}

$json = ConvertTo-Json -InputObject $manifest -Depth 6
$outPath = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { [IO.Path]::GetFullPath((Join-Path (Get-Location) $Out)) }
$outDirectory = Split-Path -Path $outPath -Parent
if (-not (Test-Path -LiteralPath $outDirectory)) { New-Item -ItemType Directory -Path $outDirectory | Out-Null }
[IO.File]::WriteAllText($outPath, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding $false))

Write-Host "Wrote $outPath"
Write-Host $json

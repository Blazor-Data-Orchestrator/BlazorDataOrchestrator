param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(WCS|WPY|VCS|VPY)-0[1-8]$')]
    [string]$ScenarioId
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot 'tests/BlazorOrchestrator.E2E.Tests/BlazorOrchestrator.E2E.Tests.csproj'
$scenarioSource = Join-Path $repositoryRoot 'tests/BlazorOrchestrator.E2E.Tests/JobScenarioTests.cs'
$declaration = "Trait(`"ScenarioId`", `"$ScenarioId`")"
$matches = @(Select-String -Path $scenarioSource -SimpleMatch $declaration)

if ($matches.Count -ne 1) {
    throw "Scenario '$ScenarioId' must have exactly one discoverable declaration; found $($matches.Count)."
}

$previousRunId = $env:BDO_TEST_RUN_ID
$env:BDO_TEST_RUN_ID = "$ScenarioId-$([Guid]::NewGuid().ToString('N'))"

try {
    Write-Host "Running $ScenarioId with BDO_TEST_RUN_ID=$env:BDO_TEST_RUN_ID"
    & dotnet test --project $testProject -- --filter-trait "ScenarioId=$ScenarioId"
    if ($LASTEXITCODE -ne 0) {
        throw "Scenario '$ScenarioId' failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -eq $previousRunId) {
        Remove-Item Env:BDO_TEST_RUN_ID -ErrorAction SilentlyContinue
    }
    else {
        $env:BDO_TEST_RUN_ID = $previousRunId
    }
}
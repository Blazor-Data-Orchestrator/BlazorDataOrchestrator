# Automated Job Tests

The repository uses xUnit v3 with Microsoft.Testing.Platform on .NET 10. `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` provide Visual Studio Test Explorer discovery. The shared `BlazorOrchestrator.Testing` project contains fixtures only and intentionally has no tests.

## Visual Studio Test Explorer

1. Open `BlazorDataOrchestrator.slnx` in Visual Studio.
2. Build the solution with **Build > Build Solution**.
3. Open **Test > Test Explorer** and allow discovery to finish.
4. Group by **Project**, **Traits**, or **Class**.
5. Search for an ID such as `WCS-01`; each of the 32 IDs resolves to one test.
6. Right-click the selected test and choose **Run** or **Debug**.

The fixture creates a unique `BDO_TEST_RUN_ID` when Visual Studio does not provide one. Temporary files are placed below `%TEMP%\bdo-tests\<run-id>` and each test deletes only its own directory.

If Test Explorer is empty, restore NuGet packages, build `BlazorDataOrchestrator.slnx`, verify the three executable test projects are loaded, inspect build output for adapter errors, then close and reopen Test Explorer. No separate xUnit Visual Studio extension is required.

## Run One Scenario

From the repository root:

```powershell
./scripts/Test-IsolatedScenario.ps1 -ScenarioId WCS-01
```

The wrapper validates that the ID has exactly one test declaration, creates a unique run ID, runs only that trait, and restores the previous environment value. The equivalent direct Microsoft.Testing.Platform command is:

```powershell
dotnet test --project tests/BlazorOrchestrator.E2E.Tests/BlazorOrchestrator.E2E.Tests.csproj -- --filter-trait "ScenarioId=WCS-01"
```

The `--` before runner options is required with the SDK 10 project-oriented command.

## Run A Suite

```powershell
dotnet test --project tests/BlazorDataOrchestrator.Core.Tests/BlazorDataOrchestrator.Core.Tests.csproj
dotnet test --project tests/BlazorOrchestrator.Web.Tests/BlazorOrchestrator.Web.Tests.csproj
dotnet test --project tests/BlazorOrchestrator.E2E.Tests/BlazorOrchestrator.E2E.Tests.csproj
```

Run one category by forwarding its trait to Microsoft.Testing.Platform:

```powershell
dotnet test --project tests/BlazorOrchestrator.Web.Tests/BlazorOrchestrator.Web.Tests.csproj -- --filter-trait "Category=Integration"
```

The matrix does not call a live AI provider. AI scenarios use scripted source responses and verify request count and update context.
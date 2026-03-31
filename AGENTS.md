# Copilot instructions

This repository is set up to use Aspire. Aspire is an orchestrator for the entire application and will take care of configuring dependencies, building, and running the application. The resources that make up the application are defined in `apphost.cs` including application code and external dependencies.

## CLI-first principle
IMPORTANT! **Always prefer CLI commands over MCP server tools** when both can accomplish the same task. CLI commands (`aspire`, `dotnet`, `az`, `azd`, etc.) are faster, consume fewer tokens, and produce more concise output. Only fall back to MCP tools when CLI cannot accomplish the task or when MCP provides significantly richer information that CLI cannot.

## General recommendations for working with Aspire
1. Before making any changes always run the apphost using `aspire run` and inspect the state of resources to make sure you are building from a known state.
1. Changes to the _apphost.cs_ file will require a restart of the application to take effect.
2. Make changes incrementally and run the aspire application using the `aspire run` command to validate changes.
3. Use CLI commands first to check the status of resources and debug issues. Only use Aspire MCP tools when CLI output is insufficient.

## Running the application
To run the application run the following command:

```
aspire run
```

If there is already an instance of the application running it will prompt to stop the existing instance. You only need to restart the application if code in `apphost.cs` is changed, but if you experience problems it can be useful to reset everything to the starting state.

## Checking resources
Prefer using CLI commands (e.g., `aspire run` output, `dotnet` commands, container CLI tools) to check resource status. Only fall back to the _list resources_ MCP tool when CLI is insufficient. If a resource is not running as expected, try CLI-based restarts before using the _execute resource command_ MCP tool.

## Listing integrations
IMPORTANT! When a user asks you to add a resource to the app model, prefer using `dotnet package search` or checking NuGet directly via CLI to find available integration packages and their versions. You should try to use the version of the integration which aligns with the version of the Aspire.AppHost.Sdk. Some integration versions may have a preview suffix. Only fall back to the _list integrations_ MCP tool if CLI search is insufficient. For documentation, prefer fetching official docs via URL over using the _get integration docs_ MCP tool when possible.

## Debugging issues
IMPORTANT! Aspire captures rich logs and telemetry for all resources. **Prefer CLI-based debugging first** — check terminal output, use `dotnet` CLI diagnostics, read log files directly, and use container CLI tools (`podman logs`, etc.) before resorting to MCP tools. Only use the following MCP tools when CLI-based approaches are insufficient:

1. _list structured logs_; use this tool to get details about structured logs.
2. _list console logs_; use this tool to get details about console logs.
3. _list traces_; use this tool to get details about traces.
4. _list trace structured logs_; use this tool to get logs related to a trace

## Other Aspire MCP tools
Only use these when CLI alternatives are not available:

1. _select apphost_; use this tool if working with multiple app hosts within a workspace.
2. _list apphosts_; use this tool to get details about active app hosts.

## Playwright MCP server

The playwright MCP server is available but should only be used when you need to perform functional UI investigations that cannot be done via CLI or API calls (e.g., `curl`, `Invoke-WebRequest`). Prefer CLI-based HTTP requests for API testing. When UI testing is required, use the playwright MCP server for navigation — to get endpoints, check the Aspire run output or use the list resources tool.

## Updating the app host
The user may request that you update the Aspire apphost. You can do this using the `aspire update` command. This will update the apphost to the latest version and some of the Aspire specific packages in referenced projects, however you may need to manually update other packages in the solution to ensure compatibility. You can consider using the `dotnet-outdated` with the users consent. To install the `dotnet-outdated` tool use the following command:

```
dotnet tool install --global dotnet-outdated-tool
```

## Local development and Azure deployment
IMPORTANT! This project uses **Podman Desktop** as the local container runtime and is deployed to **Azure** in production. When implementing any feature, configuration, or infrastructure change, you MUST ensure it works correctly in both environments. Never hardcode assumptions about the container runtime (e.g., Docker-specific socket paths or CLI commands). Use environment-aware configuration, Aspire abstractions, and Azure-compatible services so that code runs seamlessly under Podman Desktop locally and on Azure Container Apps (or the target Azure hosting service) in production.

## Persistent containers
IMPORTANT! Consider avoiding persistent containers early during development to avoid creating state management issues when restarting the app.

## Aspire workload
IMPORTANT! The aspire workload is obsolete. You should never attempt to install or use the Aspire workload.

## Official documentation
IMPORTANT! Always prefer official documentation when available. The following sites contain the official documentation for Aspire and related components

1. https://aspire.dev
2. https://learn.microsoft.com/dotnet/aspire
3. https://nuget.org (for specific integration package details)

## Additional instructions
- See .github\instructions\csharp.instructions.md
- See .github\instructions\azure.instructions.md
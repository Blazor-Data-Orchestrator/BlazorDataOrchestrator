# Frequently Asked Questions

---

## General

### What is Blazor Data Orchestrator?

Blazor Data Orchestrator is a distributed job orchestration platform built on .NET Aspire and Blazor Server. It lets you create, schedule, and run automated jobs written in C# or Python through a web-based interface.

### What technologies does it use?

- **.NET 10** with **Blazor Server** for the web UI
- **.NET Aspire** for service orchestration
- **Azure Storage** (Blob, Queue, Table) for packages, messaging, and logs
- **SQL Server** for job metadata and configuration
- **Monaco Editor** for in-browser code editing
- **Roslyn** for C# compilation
- **Radzen** for UI components

### Do I need Azure to run this locally?

No. Aspire automatically starts an **Azurite** container (Azure Storage emulator) and a **SQL Server** container for local development. No Azure subscription is required to develop and test locally.

---

## Installation

### The app won't start — what should I check?

1. Ensure **Docker Desktop** (or Podman) is running.
2. Verify the **.NET 10 SDK** is installed: `dotnet --version`
3. Run `dotnet workload restore` from the solution root.
4. Check that ports 1433, 10000, 10001, and 10002 are not in use by other applications.
5. Review the terminal output from `aspire run` for error messages.

### Do I need to install the Aspire workload?

No. The legacy `aspire` workload is obsolete. Run `dotnet workload restore` from the solution root — this restores only the workloads the solution requires.

### Can I use an existing SQL Server instead of the container?

Yes. Update the connection string in the Web, Scheduler, and Agent `appsettings.json` files to point to your SQL Server instance. The Install Wizard will create the required database schema on first launch.

### The Install Wizard doesn't appear

Clear your browser cache and navigate to the root URL of the web application. The wizard appears when the application detects that the database schema has not been created.

---

## Job Development

### How do I add NuGet dependencies to my C# job?

Add dependencies in the `.nuspec` file within the Code Tab editor:

```xml
<dependencies>
    <dependency id="Newtonsoft.Json" version="13.0.3" />
</dependencies>
```

Alternatively, use CS-Script syntax at the top of your `.cs` file:

```csharp
//css_nuget Newtonsoft.Json
```

Dependencies are resolved automatically via `dotnet restore` at compilation and execution time.

### What is the entry point for a C# job?

The entry point is the `BlazorDataOrchestratorJob.ExecuteJob()` static method in `main.cs`.

### What is the entry point for a Python job?

The entry point is the `execute_job()` function in `main.py`.

### Can I use multiple code files?

Yes. In the online editor, you can add additional `.cs` or `.py` files alongside `main.cs`/`main.py`. All files are packaged together in the `.nupkg`.

### How do environment-specific settings work?

Jobs can include `appsettings.json`, `appsettingsProduction.json`, and `appsettingsStaging.json`. The Agent loads the appropriate file based on the job's `JobEnvironment` setting and merges in connection strings from its own configuration.

---

## Operations

### How do I trigger a job manually?

Click **Run Job Now** on the Details tab or Code tab of the Job Details dialog. This compiles (if in Code Edit mode), packages, uploads, and queues the job for immediate execution.

### How do I set up a job schedule?

1. Open the Job Details dialog.
2. Navigate to the **Schedules** tab.
3. Click **Add Schedule**.
4. Configure days of the week, start/stop time, and run interval.
5. Enable the schedule.

### Can I trigger a job from an external system?

Yes. Enable the webhook on the **Webhook** tab in Job Details. The displayed URL (`/webhook/{GUID}`) accepts HTTP GET and POST requests. Query parameters are forwarded to the job execution context.

### How do I scale job processing?

Deploy multiple Agent instances or replicas. Each agent monitors a specific queue (configured via the `QueueName` setting). You can:
- Deploy multiple replicas of the same agent for horizontal scaling on a single queue.
- Deploy separate agents with different `QueueName` values to create dedicated processing pools.

---

## Troubleshooting

### Where are job logs stored?

Job execution logs are stored in Azure Table Storage in the `JobLogs` table. You can view them in the **Logs** tab of the Job Details dialog.

### My C# job fails to compile — how do I debug?

The compilation error dialog shows the file name, line number, and error description. Common issues:
- Missing NuGet dependency — add it to the `.nuspec` file.
- Incorrect class or method name — the entry point must be `BlazorDataOrchestratorJob.ExecuteJob()`.
- Syntax errors — check the line number referenced in the error message.

### The agent isn't picking up jobs

1. Check the queue name in the agent's `appsettings.json` — it must match the queue assigned to the job.
2. Verify the Azurite or Azure Storage Queue service is running.
3. Check the agent logs in the Aspire dashboard.
4. Ensure the job is **enabled** and has been **queued** (visible in the home page table).

### Why was my job executed twice?

This typically indicates an agent crash during execution. The message visibility timeout expires (default: 5 minutes), making the message visible to another agent. The visibility timeout renewal (every 3 minutes) normally prevents this, but if the agent process terminates unexpectedly, the message will be reprocessed.

### How do I connect the AI Code Assistant?

1. Navigate to **Administration > Settings**.
2. Select your AI provider (OpenAI or Azure OpenAI).
3. Enter your API key and endpoint (for Azure OpenAI).
4. Select a model (e.g., `gpt-4`).
5. The AI button appears in the Code Tab editor toolbar.

---

*Back to [Home](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Home)*

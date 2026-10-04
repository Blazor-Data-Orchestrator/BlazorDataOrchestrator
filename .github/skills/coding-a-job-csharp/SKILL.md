---
name: coding-a-job-csharp
description: Rules for writing Blazor Data Orchestrator C# jobs (main.cs, appsettings files, .nuspec), including file targeting and strict JSON rules.
---

<!-- This file is injected verbatim into the web AI Code Assistant and the JobCreatorTemplate AI chat. Keep it accurate; contradictions here become AI bugs. -->

# Blazor Data Orchestrator C# Instructions

You are an AI assistant helping to write code for the **Blazor Data Orchestrator**. When generating code, you must strictly adhere to the project structure, configuration settings, and method signatures defined below.

## 0. File Targeting and File-Type Rules (READ FIRST)

Every request includes an **Editor Context** that lists the job's files and marks:

* the **ACTIVE file**: the file currently open in the editor, and
* the **PRIMARY code file**: `main.cs`, where all C# job code lives.

The ACTIVE file is **not** automatically the file you should change. Decide the target from the user's request.

### Where each kind of content belongs

| Content | Allowed file(s) |
|---|---|
| C# code (classes, methods, `using` directives, `// NUGET:` headers) | `main.cs` (or another `.cs` file listed in the Editor Context) |
| Runtime configuration (connection strings, API keys, feature flags) | `appsettings.json`, `appsettings.Development.json`, `appsettings.Staging.json`, `appsettings.Production.json` |
| NuGet package manifest | the `.nuspec` file listed in the Editor Context |

### Hard rules

1. **NEVER** write C# code, or code of any kind, into a `.json`, `.nuspec`, or `.txt` file.
2. If the user asks to change code, logic, behavior, error handling, logging, or packages, change **`main.cs`**, even when the ACTIVE file is a `.json` file.
3. Change a `.json` file **only** when the user explicitly asks to change settings or configuration.
4. Every `.json` file you output **must be strictly valid JSON (RFC 8259)**:
   * a single root object `{ ... }`
   * property names and string values in **double quotes**
   * **no** comments (`//` or `/* */`)
   * **no** trailing commas
   * **no** code, markdown, or ellipses (`...`)
   * escape backslashes and quotes inside strings (`"C:\\temp"`, `"say \"hi\""`)
5. In appsettings files, keep the reserved connection strings `blobs`, `queues`, `tables`, and `blazororchestratordb` **present and blank** (`""`). Never delete them.
6. Start from the current content of the file shown in the Editor Context and keep every existing setting and line unless the user asked to remove it. Always return the **complete** content of every file you change. Never return a diff, a fragment, or placeholders such as `// ... existing code ...`.
7. Only use file names that appear in the Editor Context. Do not invent new files.

### Output format

* **C# code for `main.cs`**: wrap the full file in `###UPDATED CODE BEGIN###` and `###UPDATED CODE END###`. These markers **always** mean `main.cs`, whatever file is ACTIVE.
* **Any other file**: wrap the full file in `###UPDATED FILE BEGIN: <file name>###` and `###UPDATED FILE END###`, using the exact file name from the Editor Context.
* **`.nuspec`**: use `###NUSPEC BEGIN###` and `###NUSPEC END###` as described in section 1b.
* Put a fenced code block with the correct language (`csharp`, `json`, `xml`) **inside** each marker pair.
* Emit one block per changed file. Do not emit a block for a file you did not change.

Example: the user asks for a new API key setting **and** code that reads it:

###UPDATED CODE BEGIN###
```csharp
// full main.cs here
```
###UPDATED CODE END###

###UPDATED FILE BEGIN: appsettings.json###
```json
{
  "ConnectionStrings": {
    "blobs": "",
    "queues": "",
    "tables": "",
    "blazororchestratordb": ""
  },
  "WeatherApi": {
    "ApiKey": ""
  }
}
```
###UPDATED FILE END###

### Self-check before you respond

* [ ] Is all C# code inside `###UPDATED CODE BEGIN###` (main.cs) or a `.cs` file block?
* [ ] Did I avoid putting any code in a `.json` file?
* [ ] Would every `.json` block pass a strict JSON parser?
* [ ] Did I return complete files only for the files I changed?

## 1. NuGet Dependencies
If your solution requires 3rd party libraries (NuGet packages), you MUST indicate them at the very top of `main.cs` using the syntax `// NUGET: <PackageId>, <Version>`.

* **Do not** assume packages are pre-installed.
* **Always** specify a stable version.
* **Example Header:**
    ```csharp
    // NUGET: Newtonsoft.Json, 13.0.3
    // NUGET: HtmlAgilityPack, 1.11.46
    using System;
    using Newtonsoft.Json;
    ...
    ```

## 1b. NuGet Package Configuration (`.nuspec` File)

When your code requires third-party NuGet packages, you MUST provide **two things**:

### A. Comment Headers in `main.cs` (required — keep existing behavior)

Place `// NUGET:` comments at the very top of `main.cs`:

```csharp
// NUGET: SendGrid, 9.29.3
// NUGET: HtmlAgilityPack, 1.11.72
using System;
using SendGrid;
```

### B. `.nuspec` File Content (required — for web compilation)

You MUST also provide the `.nuspec` file content so the web editor can resolve and download the NuGet packages at compile time. Wrap the `.nuspec` XML between the markers `###NUSPEC BEGIN###` and `###NUSPEC END###`:

###NUSPEC BEGIN###
```xml
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>BlazorDataOrchestrator.Job</id>
    <version>1.0.0</version>
    <authors>BlazorDataOrchestrator</authors>
    <description>Auto-generated job package (csharp)</description>
    <contentFiles>
      <files include="**/*" buildAction="Content" copyToOutput="true" />
    </contentFiles>
    <dependencies>
      <group targetFramework="net10.0">
        <dependency id="SendGrid" version="9.29.3" />
        <dependency id="HtmlAgilityPack" version="1.11.72" />
      </group>
    </dependencies>
  </metadata>
</package>
```
###NUSPEC END###

**Rules:**
- The `targetFramework` MUST be `net10.0`.
- Every package listed in `// NUGET:` headers MUST also appear as a `<dependency>` in the `.nuspec`.
- Use stable (non-prerelease) versions only.
- If updating existing code that already has packages, preserve existing dependencies and add new ones.

## 2. C# Code Requirements (`main.cs`)

If the selected language is **C#**, the generated code must meet the following strict criteria to ensure it can be executed by the system's `OnRunCode` harness.

When the response is a code update, provide the full code in a block surrounded by ###UPDATED CODE BEGIN### and ###UPDATED CODE END###
### Class and Method Signature

You must define a class named `BlazorDataOrchestratorJob`. This class **must** expose a public static asynchronous method named `ExecuteJob` with the exact signature below:

```csharp
public class BlazorDataOrchestratorJob
{
    public static async Task<List<string>> ExecuteJob(
        string appSettings, 
        int jobAgentId, 
        int jobId, 
        int jobInstanceId, 
        int jobScheduleId,
        string webAPIParameter)
    {
        // Your logic here
    }
}

```
Do not change the code in Home.razor.

### Source File Layout

* **Write the whole job into `CodeCSharp/main.cs`.** That is the file the AI code editor writes and the only file the template's **Run** button compiles.
* At run time the Agent compiles **every** `.cs` file under `CodeCSharp` together into a single assembly, so hand-authored helper files placed beside `main.cs` are packaged and compiled. They cannot be exercised with the template's **Run** button, which still compiles only the file open in the editor.
* No filename is load-bearing — `main.cs` is a convention, not a requirement.
* Exactly **one** type named `BlazorDataOrchestratorJob` may exist across all files. Two are rejected with an ambiguity error, even in different namespaces.

### Package Validation Rules

A job package is refused at build time when any of these hold:

* No `.cs` file in `CodeCSharp` and no `.py` file in `CodePython` — code files sitting directly at the job root are named in the error.
* `dependencies.json` or the job `.csproj` cannot be parsed.
* Any dependency version contains `*` — pin an exact version, for example `13.0.4`. Bracketed ranges such as `[1.2.3]` remain valid.
* A file being packaged is a compiled binary, whatever its extension. Declare a NuGet dependency instead.

Declare `Microsoft.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.SqlServer` explicitly at version `10.0.11`, the version `BlazorDataOrchestrator.Core` uses. If the job omits them, that same version is injected by default.

### Dependencies & Context

* The code must return a `List<string>` containing log messages.
* The code receives `appSettings` as a raw JSON string. You must parse this to retrieve connection strings.
* You should assume the presence of `BlazorDataOrchestrator.Core` and `Microsoft.EntityFrameworkCore` namespaces.

## 2b. AppSettings Files

A job has exactly these four configuration files, using the dotted naming convention:

- `appsettings.json` — shared base
- `appsettings.Development.json`
- `appsettings.Staging.json`
- `appsettings.Production.json`

Never use `appsettingsProduction.json` or `appsettingsStaging.json`; those names are no longer recognised.

Every appsettings file must be strictly valid JSON (see section 0). Leave the four reserved connection strings **blank** — the executing host always overwrites them:

```json
{
  "ConnectionStrings": {
    "blobs": "",
    "queues": "",
    "tables": "",
    "blazororchestratordb": ""
  }
}
```

All other settings (API keys, feature flags, custom connection strings) are used exactly as packaged.

## 3. Reference Implementation

### Valid C# Code Example

Use the following example as a template for structure, error handling, logging, and `DbContext` initialization.

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BlazorDataOrchestrator.Core;
using BlazorDataOrchestrator.Core.Data;

public class BlazorDataOrchestratorJob
{
    public static async Task<List<string>> ExecuteJob(string appSettings, int jobAgentId, int jobId, int jobInstanceId, int jobScheduleId, string webAPIParameter)
    {
        // List to collect log messages
        var logs = new List<string>();

        // Initialize Connection strings
        string connectionString = "";
        string tableConnectionString = "";

        try
        {
            // Deserialize appSettings to extract connection strings
            var settings = JsonSerializer.Deserialize<JsonElement>(appSettings);

            // Extract connection strings
            if (settings.TryGetProperty("ConnectionStrings", out var connStrings))
            {
                // Get specific connection strings
                if (connStrings.TryGetProperty("blazororchestratordb", out var defaultConn))
                {
                    // Primary database connection string
                    connectionString = defaultConn.GetString() ?? "";
                }

                // Table storage connection string
                if (connStrings.TryGetProperty("tables", out var tableConn))
                {
                    // Table storage connection string
                    tableConnectionString = tableConn.GetString() ?? "";
                }
            }
        }
        catch { }

        // Initialize database context for logging
        ApplicationDbContext? dbContext = null;

        // Create DbContext if connection string is available
        if (!string.IsNullOrEmpty(connectionString))
        {
            // Set up DbContext options
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            // Use SQL Server provider
            optionsBuilder.UseSqlServer(connectionString);

            // Instantiate the DbContext
            dbContext = new ApplicationDbContext(optionsBuilder.Options);
        }

        try
        {
            // Get the jobId from the jobInstanceId if not provided (validates the relationship)
            if (jobId <= 0 && jobInstanceId > 0 && dbContext != null)
            {
                // Fetch jobId using JobManager utility
                jobId = await JobManager.GetJobIdFromInstanceAsync(dbContext, jobInstanceId);
            }

            // Log job start
            await JobManager.LogProgress(dbContext, jobInstanceId, "Job started", "Info", tableConnectionString);
            logs.Add("Job started");

            // Log execution details
            string execInfo = $"Executing Job ID: {jobId}, Instance: {jobInstanceId}, Schedule: {jobScheduleId}, Agent: {jobAgentId}";
            Console.WriteLine(execInfo);
            await JobManager.LogProgress(dbContext, jobInstanceId, execInfo, "Info", tableConnectionString);
            logs.Add(execInfo);

            // Log partition key info
            string partitionKeyInfo = $"Log partition key: {jobId}-{jobInstanceId}";
            Console.WriteLine(partitionKeyInfo);
            await JobManager.LogProgress(dbContext, jobInstanceId, partitionKeyInfo, "Info", tableConnectionString);
            logs.Add(partitionKeyInfo);

            // Check for previous run time
            JobDatum? lastRunDatum = null;
            if (dbContext != null && jobId > 0)
            {
                // Retrieve the last run time from JobData
                lastRunDatum = await dbContext.JobData
                    .FirstOrDefaultAsync(d => d.JobId == jobId && d.JobFieldDescription == "Last Job Run Time");

                // Log previous run time if available
                if (lastRunDatum != null && lastRunDatum.JobDateValue.HasValue)
                {
                    // Convert to local time for logging
                    var localTime = lastRunDatum.JobDateValue.Value.ToLocalTime();

                    // Log previous run time
                    string prevRunMsg = $"Previous time the job was run: {localTime:MM/dd/yyyy hh:mm}{localTime.ToString("tt").ToLower()}";
                    Console.WriteLine(prevRunMsg);
                    await JobManager.LogProgress(dbContext, jobInstanceId, prevRunMsg, "Info", tableConnectionString);
                    logs.Add(prevRunMsg);
                }
            }

            // Fetch weather data for the specified location
            await JobManager.LogProgress(dbContext, jobInstanceId, "Fetching weather data for 90746", "Info", tableConnectionString);
            logs.Add("Fetching weather data for 90746");

            // Set up HTTP client                 
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "BlazorDataOrchestrator/1.0");
                
            // Using wttr.in as a free weather API (weather.com requires API key)
            string weatherUrl = "https://wttr.in/90746?format=j1";
                
            try
            {
                // Make the HTTP GET request
                var response = await httpClient.GetStringAsync(weatherUrl);

                try
                {
                    // Parse the JSON response
                    var weatherData = JsonSerializer.Deserialize<JsonElement>(response);
                        
                    // Extract current weather information
                    var currentCondition = weatherData.GetProperty("current_condition")[0];
                    string tempC = currentCondition.GetProperty("temp_C").GetString() ?? "";
                    string tempF = currentCondition.GetProperty("temp_F").GetString() ?? "";
                    string humidity = currentCondition.GetProperty("humidity").GetString() ?? "";
                    string weatherDesc = currentCondition.GetProperty("weatherDesc")[0].GetProperty("value").GetString() ?? "";

                    // Log the weather information
                    string weatherInfo = $"90746 - Temperature: {tempF}°F ({tempC}°C), Humidity: {humidity}%, Conditions: {weatherDesc}";
                    Console.WriteLine(weatherInfo);
                    await JobManager.LogProgress(dbContext, jobInstanceId, weatherInfo, "Info", tableConnectionString);
                    logs.Add(weatherInfo);
                }
                catch (Exception)
                {
                    string notFoundMsg = "Location '90746' was not found.";
                    Console.WriteLine(notFoundMsg);
                    await JobManager.LogProgress(dbContext, jobInstanceId, notFoundMsg, "Warning", tableConnectionString);
                    logs.Add(notFoundMsg);
                }
            }
            catch (HttpRequestException ex)
            {
                string errorMsg = $"Failed to fetch weather data: {ex.Message}";
                Console.WriteLine(errorMsg);
                await JobManager.LogProgress(dbContext, jobInstanceId, errorMsg, "Warning", tableConnectionString);
                logs.Add(errorMsg);
            }

            // Job completed successfully
            await JobManager.LogProgress(dbContext, jobInstanceId, "Job completed successfully!", "Info", tableConnectionString);
            logs.Add("Job completed successfully!");
        }
        catch (Exception ex)
        {
            string errorMsg = $"Job execution error: {ex.Message}";
            Console.WriteLine(errorMsg);
            await JobManager.LogError(dbContext, jobInstanceId, errorMsg, ex.StackTrace, tableConnectionString);
            logs.Add(errorMsg);
            throw;
        }
        finally
        {
            // Update Last Job Run Time
            try
            {
                // Update the last run time in JobData
                if (dbContext != null && jobId > 0)
                {
                    // Retrieve existing datum
                    var runDatum = await dbContext.JobData
                        .FirstOrDefaultAsync(d => d.JobId == jobId && d.JobFieldDescription == "Last Job Run Time");

                    // Create new datum if it doesn't exist
                    if (runDatum == null)
                    {
                        // Create new JobDatum
                        runDatum = new JobDatum
                        {
                            JobId = jobId,
                            JobFieldDescription = "Last Job Run Time",
                            CreatedBy = "System",
                            CreatedDate = DateTime.UtcNow
                        };

                        // Add to DbContext
                        dbContext.JobData.Add(runDatum);
                    }

                    // Update the date value
                    runDatum.JobDateValue = DateTime.UtcNow;
                    runDatum.UpdatedBy = "System";
                    runDatum.UpdatedDate = DateTime.UtcNow;

                    // Save changes to the database
                    await dbContext.SaveChangesAsync();
                }
            }
            catch { }

            dbContext?.Dispose();
        }

        // Return the collected logs
        return logs;
    }
}

```

## 4. Execution Logic (`OnRunCode`)

The code you generate interacts with the main application via the `OnRunCode` method. Understanding this harness helps ensure your generated code integrates correctly (e.g., understanding how `appSettings` are loaded and passed, and how logs are displayed).

**Do not modify this execution logic.** Use it only for context.

```csharp
private async Task OnRunCode(RadzenSplitButtonItem? item)
{
    // Save before running
    if (!await OnSaveFile())
    {
        logOutput += $"[{DateTime.Now:HH:mm:ss}] Save failed. Aborting run.\n";
        return;
    }

    var environment = item?.Value?.ToString() ?? "Development";
    logOutput = $"[{DateTime.Now:HH:mm:ss}] Running code in {environment} mode...\n";
    
    isExecuting = true;
    StateHasChanged();
    await Task.Delay(1); // Allow UI to update

    if (codeEditor != null)
    {
        var currentCode = await codeEditor.GetCodeAsync();
        logOutput += $"[{DateTime.Now:HH:mm:ss}] Code retrieved ({currentCode?.Length ?? 0} characters)\n";

        // 1. Get AppSettings
        string appSettingsFileName = JobEnvironments.GetFileName(environment); // e.g. appsettings.Production.json
        string appSettingsContent = "{}";
        string appSettingsFile = Path.Combine(Environment.ContentRootPath, appSettingsFileName);
        
        if (File.Exists(appSettingsFile))
        {
            appSettingsContent = await File.ReadAllTextAsync(appSettingsFile);
            logOutput += $"[{DateTime.Now:HH:mm:ss}] Loaded {appSettingsFileName}\n";
        }
        else
        {
            logOutput += $"[{DateTime.Now:HH:mm:ss}] Warning: {appSettingsFileName} not found. Using defaults.\n";
        }

        // Patch connection strings from Configuration (Aspire environment variables)
        try
        {
            var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(appSettingsContent) ?? new System.Text.Json.Nodes.JsonObject();
            
            var connString = Configuration.GetConnectionString("blazororchestratordb");
            if (!string.IsNullOrEmpty(connString))
            {
                if (jsonNode["ConnectionStrings"] == null)
                {
                    jsonNode["ConnectionStrings"] = new System.Text.Json.Nodes.JsonObject();
                }
                jsonNode["ConnectionStrings"]!["blazororchestratordb"] = connString;
            }

            var tableConnString = Configuration.GetConnectionString("tables");
            if (!string.IsNullOrEmpty(tableConnString))
            {
                if (jsonNode["ConnectionStrings"] == null)
                {
                    jsonNode["ConnectionStrings"] = new System.Text.Json.Nodes.JsonObject();
                }
                jsonNode["ConnectionStrings"]!["tables"] = tableConnString;
            }
            
            appSettingsContent = jsonNode.ToJsonString();
            logOutput += $"[{DateTime.Now:HH:mm:ss}] Patched connection strings from environment\n";
        }
        catch (Exception ex)
        {
            logOutput += $"[{DateTime.Now:HH:mm:ss}] Warning: Failed to patch connection strings: {ex.Message}\n";
        }

        // 2. Create Job Instance
        int jobInstanceId = 0;
        if (jobManager != null)
        {
            try
            {
                string jobName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "DesignerJob";
                jobInstanceId = await jobManager.CreateDesignerJobInstanceAsync(jobName);
                currentJobInstanceId = jobInstanceId;
                logOutput += $"[{DateTime.Now:HH:mm:ss}] Created Job Instance ID: {jobInstanceId}\n";
            }
            catch (Exception ex)
            {
                logOutput += $"[{DateTime.Now:HH:mm:ss}] Error creating job instance: {ex.Message}\n";
                return;
            }
        }
        else
        {
                logOutput += $"[{DateTime.Now:HH:mm:ss}] Warning: JobManager not initialized. Skipping logging.\n";
        }

        // 3. Execute Code
        List<string> results = new List<string>();
        bool hasError = false;
        try
        {
            if (selectedLanguage == "csharp")
            {
                logOutput += $"[{DateTime.Now:HH:mm:ss}] Executing ExecuteJob...\n";
                results = await BlazorDataOrchestratorJob.ExecuteJob(appSettingsContent, -1, -1, currentJobInstanceId, -1, "");
            }
            else if (selectedLanguage == "python")
            {
                logOutput += $"[{DateTime.Now:HH:mm:ss}] Executing Python job...\n";

                var runnerScript = @"
import sys
import main
import traceback

if len(sys.argv) < 3:
    print('Usage: runner.py <app_settings_path> <job_instance_id>')
    sys.exit(1)

app_settings_path = sys.argv[1]
job_instance_id = int(sys.argv[2])

try:
    with open(app_settings_path, 'r') as f:
        app_settings = f.read()
        
    main.execute_job(app_settings, -1, -1, job_instance_id, -1)
except Exception as e:
    print(f'Error executing job: {e}')
    traceback.print_exc()
";
                var pythonCodePath = GetCodeFolderPath("python");
                var runnerPath = Path.Combine(pythonCodePath, "runner.py");
                await File.WriteAllTextAsync(runnerPath, runnerScript);

                try
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"\"{runnerPath}\" \"{appSettingsFile}\" {currentJobInstanceId}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = pythonCodePath
                    };

                    using var process = new System.Diagnostics.Process { StartInfo = startInfo };
                    process.Start();

                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();

                    if (!string.IsNullOrEmpty(output))
                    {
                        results.Add(output);
                        logOutput += output + "\n";
                    }

                    if (!string.IsNullOrEmpty(error))
                    {
                        results.Add("ERROR:\n" + error);
                        logOutput += "ERROR:\n" + error + "\n";
                    }
                }
                catch (Exception ex)
                {
                    logOutput += $"[{DateTime.Now:HH:mm:ss}] Error launching python: {ex.Message}\n";
                }
                finally
                {
                    if (File.Exists(runnerPath))
                    {
                        File.Delete(runnerPath);
                    }
                }
            }

            logOutput += $"[{DateTime.Now:HH:mm:ss}] Execution complete.\n";

            if (jobManager != null && currentJobInstanceId > 0)
            {
                await jobManager.CompleteJobInstanceAsync(currentJobInstanceId, false);
            }

            // Hide the executing overlay before showing results
            isExecuting = false;
            StateHasChanged();

            // 4. Display Results
            if (results.Any())
            {
                await DialogService.OpenAsync("Execution Results", ds =>
                    @<RadzenStack Gap="10">
                        <RadzenTextArea Value="@string.Join("\n", results)" Style="width: 100%; height: 300px;" ReadOnly="true" />
                        <RadzenButton Text="Close" Click="() => ds.Close(true)" Style="width: 100px; align-self: center;" />
                    </RadzenStack>
                );
            }
        }
        catch (Exception ex)
        {
            hasError = true;
            logOutput += $"[{DateTime.Now:HH:mm:ss}] Error executing code: {ex.Message}\n";
            if (ex.InnerException != null)
            {
                logOutput += $"[{DateTime.Now:HH:mm:ss}] Inner Error: {ex.InnerException.Message}\n";
            }

            if (jobManager != null && currentJobInstanceId > 0)
            {
                await jobManager.CompleteJobInstanceAsync(currentJobInstanceId, true);
            }
        }
        finally
        {
            isExecuting = false; // Ensure it's always reset even on error
        }
    }

    StateHasChanged();
}

```
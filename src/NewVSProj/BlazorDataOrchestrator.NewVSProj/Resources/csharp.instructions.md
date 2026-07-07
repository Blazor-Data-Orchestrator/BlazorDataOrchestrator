# Blazor Data Orchestrator C# Instructions

You are an AI assistant helping to write code for the **Blazor Data Orchestrator**. When generating code, you must strictly adhere to the project structure, configuration settings, and method signatures defined below.

## 1. NuGet Dependencies
If your solution requires 3rd party libraries (NuGet packages), you MUST indicate them at the very top of the file using the syntax `// REQUIRES NUGET: <PackageId>, <Version>`.

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

### Dependencies & Context

* The code must return a `List<string>` containing log messages.
* The code receives `appSettings` as a raw JSON string. You must parse this to retrieve connection strings.
* You should assume the presence of `BlazorDataOrchestrator.Core` and `Microsoft.EntityFrameworkCore` namespaces.

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
    public static async Task<List<string>> ExecuteJob(string appSettings, int jobAgentId, int jobId, int jobInstanceId, int jobScheduleId)
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

            // ===== YOUR BUSINESS LOGIC HERE =====
            
            // Example: HTTP request
            using var httpClient = new HttpClient();
            // ... your code ...

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
            dbContext?.Dispose();
        }

        // Return the collected logs
        return logs;
    }
}
```

## 4. Best Practices

1. **Always handle exceptions** and log errors using `JobManager.LogError`
2. **Use the provided connection strings** from `appSettings` JSON
3. **Return meaningful log messages** in the `List<string>` result
4. **Clean up resources** in a `finally` block
5. **Use async/await** throughout for optimal performance

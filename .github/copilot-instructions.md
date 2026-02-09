- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.

---

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

---

# Blazor Data Orchestrator Python Instructions

## Python Dependencies

If your solution requires 3rd party libraries (PyPI packages), you MUST indicate the exact lines to be added to the `requirements.txt` file at the very top of the `main.py` file using the syntax `# REQUIREMENTS: <line content>`.

* **Do not** assume packages are pre-installed.
* **Always** specify a stable version using the `==` operator.
* **Example Header:**
```python
# ADD TO REQUIREMENTS.txt: requests==2.31.0
# ADD TO REQUIREMENTS.txt: pandas==2.1.4
import requests
import pandas as pd
...
```

## 1. Python Code Requirements (`main.py`)

The generated Python code must meet the following strict criteria to ensure it can be executed by the system's `OnRunCode` harness via the `runner.py` wrapper.

When the response is a code update, provide the full code in a block surrounded by ###UPDATED CODE BEGIN### and ###UPDATED CODE END###

### Function Signature

You must define a function named `execute_job` in `main.py` with the exact signature below:

```python
def execute_job(
    app_settings: str, 
    job_agent_id: int, 
    job_id: int, 
    job_instance_id: int, 
    job_schedule_id: int
) -> list[str]:
    # Your logic here
    return []
```

### Dependencies & Context

* **Return Type:** The function must return a `list[str]` containing log messages.
* **Input:** `app_settings` is passed as a raw JSON string. You must parse this to retrieve connection strings (`blazororchestratordb` and `tables`).
* **Environment:** The code runs in a Python environment where `pyodbc` (for SQL Server) and `azure.data.tables` (for Table Storage) may or may not be available. You must handle imports gracefully using `try-except` blocks.
* **Logging:** Logs must be printed to `stdout` (for the UI console) and persisted to the database/table storage using the `JobLogger` helper class pattern shown in the reference implementation.

## 2. Reference Implementation

### Valid Python Code Example

Use the following example as a strict template for structure, error handling, dependency management, and logging.

```python
import os
import json
import sys
import urllib.request
import urllib.error
from datetime import datetime
from typing import Optional
import uuid

# Database connection (requires pyodbc for SQL Server)
try:
    import pyodbc
    HAS_PYODBC = True
except ImportError:
    HAS_PYODBC = False

# Azure Table Storage (requires azure-data-tables)
try:
    from azure.data.tables import TableServiceClient, TableEntity
    HAS_AZURE_TABLES = True
except ImportError:
    HAS_AZURE_TABLES = False


class JobLogger:
    """Handles logging progress and errors to the database and Azure Table Storage.
    Logs are partitioned by '{JobId}-{JobInstanceId}' for efficient querying."""
    
    def __init__(self, connection_string: str, job_instance_id: int, table_connection_string: str = None):
        self.connection_string = connection_string
        self.job_instance_id = job_instance_id
        self.table_connection_string = table_connection_string
        self.connection = None
        self.job_id = 0
        self.table_client = None
        
        # Initialize database connection if available
        if HAS_PYODBC and connection_string:
            try:
                self.connection = pyodbc.connect(connection_string)
                self.job_id = self._get_job_id_from_instance()
            except Exception as e:
                print(f"Warning: Could not connect to database: {e}")
        
        # Initialize Azure Table Storage client
        if HAS_AZURE_TABLES and table_connection_string:
            try:
                table_service = TableServiceClient.from_connection_string(table_connection_string)
                self.table_client = table_service.create_table_if_not_exists("JobLogs")
            except Exception as e:
                print(f"Warning: Could not connect to Azure Table Storage: {e}")
    
    def _get_job_id_from_instance(self) -> int:
        """Get the job_id from the job_instance_id."""
        if not self.connection or self.job_instance_id <= 0:
            return 0
        try:
            cursor = self.connection.cursor()
            cursor.execute("""
                SELECT js.JobId 
                FROM JobInstance ji 
                JOIN JobSchedule js ON ji.JobScheduleId = js.Id 
                WHERE ji.Id = ?
            """, self.job_instance_id)
            row = cursor.fetchone()
            return row[0] if row else 0
        except Exception:
            return 0
    
    def _get_partition_key(self) -> str:
        """Get the partition key in format '{JobId}-{JobInstanceId}'."""
        return f"{self.job_id}-{self.job_instance_id}"
    
    def log_progress(self, message: str, level: str = "Info"):
        """Log progress message to console and Azure Table Storage."""
        timestamp = datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S")
        print(f"[{timestamp}] [{level}] {message}")
                
        if self.table_client and self.job_id > 0:
            try:
                entity = {
                    "PartitionKey": self._get_partition_key(),
                    "RowKey": str(uuid.uuid4()),
                    "Action": "JobProgress",
                    "Details": message,
                    "Level": level,
                    "Timestamp": datetime.utcnow(),
                    "JobId": self.job_id,
                    "JobInstanceId": self.job_instance_id
                }
                self.table_client.create_entity(entity)
            except Exception as e:
                print(f"Warning: Failed to log to Azure Table Storage: {e}")
    
    def log_error(self, message: str, stack_trace: str = ""):
        """Log error to console and Azure Table Storage."""
        timestamp = datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S")
        print(f"[{timestamp}] [ERROR] {message}")
        
        if self.table_client and self.job_id > 0:
            try:
                error_message = f"{message}\n{stack_trace}" if stack_trace else message
                entity = {
                    "PartitionKey": self._get_partition_key(),
                    "RowKey": str(uuid.uuid4()),
                    "Action": "JobError",
                    "Details": error_message,
                    "Level": "Error",
                    "Timestamp": datetime.utcnow(),
                    "JobId": self.job_id,
                    "JobInstanceId": self.job_instance_id
                }
                self.table_client.create_entity(entity)
            except Exception as e:
                print(f"Warning: Failed to log error to Azure Table Storage: {e}")
    
    def close(self):
        """Close database connection."""
        if self.connection:
            self.connection.close()


def execute_job(app_settings: str, job_agent_id: int, job_id: int, job_instance_id: int, job_schedule_id: int) -> list[str]:
    """
    Execute the job with the given parameters.
    
    Args:
        app_settings: JSON string containing application settings including connection strings
        job_agent_id: The ID of the job agent executing this job
        job_id: The ID of the job
        job_instance_id: The ID of this specific job instance
        job_schedule_id: The ID of the job schedule
    """
    logs = []
    
    # Parse connection strings from app_settings
    connection_string = ""
    table_connection_string = ""
    try:
        settings = json.loads(app_settings) if app_settings else {}
        connection_strings = settings.get("ConnectionStrings", {})
        connection_string = connection_strings.get("blazororchestratordb", "")
        table_connection_string = connection_strings.get("tables", "")
    except json.JSONDecodeError:
        pass
    
    logger = JobLogger(connection_string, job_instance_id, table_connection_string)
    
    try:
        logger.log_progress("Job started")
        logs.append("Job started")
        
        # ===== YOUR BUSINESS LOGIC HERE =====
        
        logger.log_progress("Job completed successfully!")
        logs.append("Job completed successfully!")
        
    except Exception as e:
        import traceback
        error_msg = f"Job execution error: {str(e)}"
        stack_trace = traceback.format_exc()
        logger.log_error(error_msg, stack_trace)
        logs.append(error_msg)
        raise
    finally:
        logger.close()
    
    return logs
```

## 3. Best Practices

1. **Handle import errors gracefully** with try-except blocks
2. **Use the provided connection strings** from `app_settings` JSON
3. **Return meaningful log messages** in the `list[str]` result
4. **Clean up resources** using the `close()` method
5. **Use the JobLogger class** for consistent logging
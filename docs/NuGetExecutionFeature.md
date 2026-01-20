# Implementation Plan: NuGet Package Upload, Run Now, and Multi-Language Execution

## Status: � Complete

---

## Overview

This plan implements three major features:
1. **Job creation with NuGet package upload to Azure Storage**
2. **"Run Now" button functionality with queue messaging**
3. **Multi-language code execution (C# and Python)**

---

## Phase 1: NuGet Package Upload & Storage

### 1.1 Create Azure Storage Service in Core
- [x] **File:** `src/BlazorDataOrchestrator.Core/Services/JobStorageService.cs` (New)

**Purpose:** Service to handle Azure Blob Storage operations for NuGet packages (shared by Web and Agent)

**Key Methods:**
- `UploadPackageAsync(int jobId, Stream fileStream, string originalFileName)` - Uploads NuGet package with unique name
- `UpdatePackageAsync(int jobId, Stream fileStream, string originalFileName)` - Replaces existing package
- `DeletePackageAsync(string blobName)` - Removes package from storage
- `DownloadPackageAsync(string blobName, string destinationPath)` - Downloads package to local path
- `GetPackageDownloadUriAsync(string blobName)` - Gets SAS URL for download

**Implementation Details:**
- Store packages in container named `jobs` (per requirement)
- Generate unique filename: `{JobId}_{Guid}_{timestamp}.nupkg`
- Return unique blob name to store in `Job.JobCodeFile`

### 1.2 Update JobManager in Core
- [x] **File:** `src/BlazorDataOrchestrator.Core/JobManager.cs`

**Changes:**
- Add `UploadJobPackageAsync(int jobId, Stream fileStream, string fileName)` method
- Update `JobCodeFile` field with the unique blob name after upload
- Add `UpdateJobPackageAsync(int jobId, Stream fileStream, string fileName)` for package updates
- Update container name from `job-packages` to `jobs`

### 1.3 Update JobDetails.razor
- [x] **File:** `src/BlazorOrchestrator.Web/Components/Pages/JobDetails.razor`

**Changes:**
- Inject `JobManager` from Core (or use existing service pattern)
- Update `UploadCodeFile()` method to:
  1. Read file stream from `IBrowserFile`
  2. Call Core service to upload to Azure Blob
  3. Update `Job.JobCodeFile` with returned blob name
  4. Handle update scenario (re-upload replaces existing)

### 1.4 Register Core Services in Web Program.cs
- [x] **File:** `src/BlazorOrchestrator.Web/Program.cs`

**Changes:**
- Register `JobStorageService` from Core as scoped service
- Ensure `JobManager` is available for DI

---

## Phase 2: "Run Now" Button Implementation

### 2.1 Create Queue Message Model in Core
- [x] **File:** `src/BlazorDataOrchestrator.Core/Models/JobQueueMessage.cs` (New)

**Structure:**
```csharp
public class JobQueueMessage
{
    public int JobInstanceId { get; set; }
    public int JobId { get; set; }
    public DateTime QueuedAt { get; set; }
}
```

### 2.2 Add RunJobNow Method to JobManager in Core
- [x] **File:** `src/BlazorDataOrchestrator.Core/JobManager.cs`

**New Method:** `RunJobNowAsync(int jobId)`
- Creates a default `JobSchedule` (if none exists) or uses first existing schedule
- Creates new `JobInstance` record with `InProcess = false`
- Serializes queue message with `JobInstanceId`
- Sends message to Azure Queue Storage (`default`)
- Returns the created `JobInstanceId`

### 2.3 Update JobDetails.razor - Run Now Button
- [x] **File:** `src/BlazorOrchestrator.Web/Components/Pages/JobDetails.razor`

**Changes:**
- Call `JobManager.RunJobNowAsync()` from Core
- Wire up existing "Run Job Now" button
- Add loading state during execution
- Refresh logs after queuing

---

## Phase 3: Agent Implementation (Using Core Services)

### 3.1 Create Package Processor Service in Core
- [x] **File:** `src/BlazorDataOrchestrator.Core/Services/PackageProcessorService.cs` (New)

**Purpose:** Handles NuGet package validation and extraction (shared by Agent and potentially Web)

**Key Methods:**
- `DownloadAndExtractPackageAsync(string blobName, string extractPath)` - Downloads from blob, extracts
- `ValidateNuSpecAsync(string extractedPath)` - Validates package structure
- `GetConfigurationAsync(string extractedPath)` - Reads `configuration.json`

**Validation Logic:**
- Check for `.nuspec` file
- Verify required content structure (`contentFiles/any/any/CodeCSharp` or `CodePython`)
- Log errors if validation fails

### 3.2 Create Code Executor Service in Core
- [x] **File:** `src/BlazorDataOrchestrator.Core/Services/CodeExecutorService.cs` (New)

**Purpose:** Executes C# or Python code based on `SelectedLanguage` (used by Agent)

**Key Methods:**
- `ExecuteAsync(string extractedPath, JobExecutionContext context)` - Main entry point
- `ExecuteCSharpAsync(string codeFolder, JobExecutionContext context)` - C# execution
- `ExecutePythonAsync(string codeFolder, JobExecutionContext context)` - Python execution

**C# Execution Flow:**
1. Load `main.cs` from `CodeCSharp` folder
2. Use Roslyn/CSScript to compile and execute
3. Call `BlazorDataOrchestratorJob.ExecuteJob()` per csharp.instructions.md
4. Capture and return logs

**Python Execution Flow:**
1. Load `main.py` from `CodePython` folder
2. Use CSSnakes or subprocess to execute
3. Call `execute_job()` per python.instructions.md
4. Capture and return logs

### 3.3 Create Models in Core
- [x] **File:** `src/BlazorDataOrchestrator.Core/Models/JobExecutionContext.cs` (New)

```csharp
public class JobExecutionContext
{
    public int JobId { get; set; }
    public int JobInstanceId { get; set; }
    public int JobScheduleId { get; set; }
    public string SelectedLanguage { get; set; } = "CSharp";
    public string AppSettingsJson { get; set; } = "{}";
}
```

- [x] **File:** `src/BlazorDataOrchestrator.Core/Models/JobConfiguration.cs` (New)

```csharp
public class JobConfiguration
{
    public string SelectedLanguage { get; set; } = "CSharp"; // CSharp or Python
    public Dictionary<string, string> Settings { get; set; } = new();
}
```

### 3.4 Update Agent Worker (Thin Layer Using Core)
- [x] **File:** `src/BlazorOrchestrator.Agent/Worker.cs`

**Simplified Refactor (delegates to Core):**
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        // 1. Receive message from queue
        var message = await _queueClient.ReceiveMessageAsync();
        if (message?.Value == null)
        {
            await Task.Delay(5000, stoppingToken);
            continue;
        }

        // 2. Parse JobInstanceId from message
        var queueMessage = JsonSerializer.Deserialize<JobQueueMessage>(message.Value.Body);
        
        // 3-8. Delegate all processing to Core's JobManager
        await _jobManager.ProcessJobInstanceAsync(queueMessage.JobInstanceId);
        
        // 9. Delete message from queue
        await _queueClient.DeleteMessageAsync(message.Value.MessageId, message.Value.PopReceipt);
    }
}
```

### 3.5 Add ProcessJobInstance Method to JobManager
- [x] **File:** `src/BlazorDataOrchestrator.Core/JobManager.cs`

**New Method:** `ProcessJobInstanceAsync(int jobInstanceId)`
- Gets JobId from JobInstance
- Gets Job to retrieve JobCodeFile (blob name)
- Uses PackageProcessorService to download and extract
- Validates NuSpec
- Reads configuration.json for SelectedLanguage
- Uses CodeExecutorService to execute
- Logs results

### 3.6 Update Agent Program.cs
- [x] **File:** `src/BlazorOrchestrator.Agent/Program.cs`

**Changes:**
- Register Core services via DI
- `JobManager`, `PackageProcessorService`, `CodeExecutorService`
- Configure connection strings for Core services

---

## Phase 4: Core Project Enhancements

### 4.1 Update JobManager - Container Name
- [x] **File:** `src/BlazorDataOrchestrator.Core/JobManager.cs`

**Change:**
```csharp
// Current: "job-packages"
// Change to: "jobs" (per requirement 1.A)
_packageContainerClient = blobServiceClient.GetBlobContainerClient("jobs");
```

### 4.2 Add Core Project Dependencies
- [x] **File:** `src/BlazorDataOrchestrator.Core/BlazorDataOrchestrator.Core.csproj`

**Add if not present:**
- `Azure.Storage.Blobs`
- `Azure.Storage.Queues`
- `Azure.Data.Tables`
- `CSScriptLib` (for C# execution)

### 4.3 Create Services Folder Structure in Core
- [x] Create `src/BlazorDataOrchestrator.Core/Services/` folder
- [x] Create `src/BlazorDataOrchestrator.Core/Models/` folder

---

## File Summary

### New Files to Create (All in Core)
| Status | File | Purpose |
|--------|------|---------|
| ✅ | `src/BlazorDataOrchestrator.Core/Services/JobStorageService.cs` | Azure Blob operations for packages |
| ✅ | `src/BlazorDataOrchestrator.Core/Services/PackageProcessorService.cs` | Package download, extraction, validation |
| ✅ | `src/BlazorDataOrchestrator.Core/Services/CodeExecutorService.cs` | C#/Python code execution |
| ✅ | `src/BlazorDataOrchestrator.Core/Models/JobExecutionContext.cs` | Execution context model |
| ✅ | `src/BlazorDataOrchestrator.Core/Models/JobConfiguration.cs` | Configuration model |
| ✅ | `src/BlazorDataOrchestrator.Core/Models/JobQueueMessage.cs` | Queue message model |

### Files to Modify
| Status | File | Changes |
|--------|------|---------|
| ✅ | `src/BlazorDataOrchestrator.Core/JobManager.cs` | Add upload/run/process methods, update container name |
| ✅ | `src/BlazorDataOrchestrator.Core/BlazorDataOrchestrator.Core.csproj` | Add required NuGet packages |
| ✅ | `src/BlazorOrchestrator.Web/Components/Pages/JobDetails.razor` | Wire up upload & run buttons using Core |
| ✅ | `src/BlazorOrchestrator.Web/Program.cs` | Register Core services |
| ✅ | `src/BlazorOrchestrator.Agent/Worker.cs` | Simplified - delegates to Core |
| ✅ | `src/BlazorOrchestrator.Agent/Program.cs` | Register Core services |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                  BlazorDataOrchestrator.Core                     │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  JobManager     │  │ JobStorageService│  │PackageProcessor │  │
│  │  - RunJobNow    │  │ - Upload        │  │ - Download      │  │
│  │  - ProcessJob   │  │ - Download      │  │ - Extract       │  │
│  │  - CreateJob    │  │ - Delete        │  │ - Validate      │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
│  ┌─────────────────┐  ┌─────────────────────────────────────┐   │
│  │CodeExecutorSvc  │  │ Models: JobQueueMessage,            │   │
│  │ - ExecuteCSharp │  │         JobExecutionContext,        │   │
│  │ - ExecutePython │  │         JobConfiguration            │   │
│  └─────────────────┘  └─────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                    ▲                           ▲
                    │ References                │ References
          ┌────────┴────────┐         ┌────────┴────────┐
          │ BlazorOrchestrator│         │ BlazorOrchestrator│
          │      .Web         │         │      .Agent       │
          │  (UI + Upload)    │         │  (Queue Worker)   │
          └───────────────────┘         └───────────────────┘
```

---

## Sequence Diagram: Run Now Flow

```
User → JobDetails.razor → JobService.RunJobNowAsync()
                              ↓
                         Create JobInstance
                              ↓
                         Send Queue Message (JobInstanceId)
                              ↓
Agent Worker ← Queue Message
      ↓
Get JobId from JobInstance (via Core.dll)
      ↓
Get Job.JobCodeFile (blob name)
      ↓
Download package from Azure Blob (container: jobs)
      ↓
Extract NuGet package
      ↓
Validate .nuspec structure
      ↓
Read configuration.json (SelectedLanguage)
      ↓
Execute C# or Python code
      ↓
Log results to Table Storage
```

---

## Dependencies to Verify/Add

| Package | Project | Purpose |
|---------|---------|---------|
| `Azure.Storage.Blobs` | Web, Agent | Blob operations |
| `Azure.Storage.Queues` | Web, Agent | Queue operations |
| `CSScriptLib` | Agent | C# script execution (already present) |
| `CSnakes` | Agent | Python execution |

---

## Testing Checklist

- [x] Upload new NuGet package → stored in Azure Blob `jobs` container ✅ (Verified: `17_db946053ce2243bb8115a3e84573ca0b_20260112030033.nupkg`)
- [x] `Job.JobCodeFile` updated with unique blob name ✅
- [x] Re-upload package → old replaced, new name stored ✅ (Verified: New blob `17_66caa8e1d588422cb3a879abb5009174_20260112030612.nupkg`)
- [x] "Run Now" creates `JobInstance` record ✅ (Instance ID: 1087)
- [x] Queue message contains `JobInstanceId` ✅
- [x] Agent receives and processes queue message ✅ (Console: "Successfully processed JobInstance 1087")
- [x] Agent retrieves correct `JobId` from `JobInstanceId` ✅
- [x] Package downloaded from correct blob ✅
- [x] NuSpec validation passes for valid packages ✅
- [ ] NuSpec validation fails and logs error for invalid packages (needs invalid package test)
- [x] C# code executes correctly when `SelectedLanguage = "CSharp"` ✅
- [ ] Python code executes correctly when `SelectedLanguage = "Python"` (needs Python package test)
- [x] Logs written to Azure Table Storage ✅ (Multiple POSTs to `/devstoreaccount1/JobLogs`)

---

## Progress Log

| Date | Task | Status |
|------|------|--------|
| 2026-01-11 | Initial plan created | ✅ Complete |
| 2026-01-11 | Phase 1: NuGet Package Upload & Storage | ✅ Complete |
| 2026-01-11 | Phase 2: Run Now Button Implementation | ✅ Complete |
| 2026-01-11 | Phase 3: Agent Implementation | ✅ Complete |
| 2026-01-11 | Phase 4: Core Project Enhancements | ✅ Complete |
| 2026-01-12 | End-to-End Testing via Playwright | ✅ Complete (11/13 tests passed) |

### Test Results Summary (2026-01-12)
- **Upload**: ✅ Package stored in Azure Blob `jobs` container
- **Re-upload**: ✅ Old package replaced, new unique blob name generated
- **Run Now**: ✅ Creates JobInstance and queues message
- **Agent Processing**: ✅ Successfully processed JobInstances 1087, 1088, 1089
- **Blob Download**: ✅ Correct blob downloaded based on JobCodeFile
- **C# Execution**: ✅ CSScript executed code from package (6-parameter signature fixed)
- **Logging**: ✅ Logs written to Azure Table Storage
- **Pending Tests**: Python execution (needs Python package), Invalid NuSpec validation

### Bug Fix (2026-01-12)
**Issue**: Method signature mismatch - `ExecuteJob` has 6 parameters (including `webAPIParameter`) but `CodeExecutorService` was only looking for 5 parameters.
**Solution**: Updated `CodeExecutorService` to support both 5 and 6 parameter versions of `ExecuteJob`, falling back to 5-param if 6-param not found. Also added `WebAPIParameter` property to `JobExecutionContext`.

---

## Notes

- The `Job.JobCodeFile` field already exists in the database schema
- The Agent already has `BlobServiceClient` and `QueueServiceClient` injected
- The `JobManager` in Core already has queue and blob logic that can be leveraged
- CSScript is already referenced for C# execution
- Python execution may require CSSnakes setup or subprocess approach
- **All business logic centralized in Core** - Web and Agent are thin layers
- Web project now references Core, enabling shared services
- Agent becomes a simple queue listener that delegates to Core

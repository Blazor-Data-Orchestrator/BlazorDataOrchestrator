# BlazorOrchestrator.Scheduler Implementation Plan

## Overview

This document outlines the plan to implement a job scheduler service based on the reference WarehouseOrchestratorScheduler project, adapted for the BlazorOrchestrator architecture with custom queue assignment logic.

---

## Current State Analysis

### What's Already Implemented ✅

| Component | Status | Description |
|-----------|--------|-------------|
| Entity Models | ✅ Complete | Jobs, JobSchedule, JobInstance, JobLogs, JobAgents, JobData, JobGroups, JobJobGroup |
| DbContext | ✅ Complete | SchedulerDbContext with all DbSets |
| Worker Service | ✅ Partial | Basic scheduling loop with queue integration |
| Configuration | ✅ Complete | Azure Storage and queue settings in appsettings.json |

### Current Worker Logic Flow

```mermaid
flowchart TD
    A[Start Scheduler Loop] --> B[Query Enabled JobSchedules]
    B --> C[Mark Stuck Instances as Error]
    C --> D{For Each Schedule}
    D --> E{Is Today Scheduled?}
    E -- No --> D
    E -- Yes --> F{Within Time Window?}
    F -- No --> D
    F -- Yes --> G{Instance Already In Process?}
    G -- Yes --> D
    G -- No --> H{Should Schedule?}
    H -- No --> D
    H -- Yes --> I[Create JobInstance]
    I --> J[Determine Queue Name]
    J --> K[Enqueue to Azure Queue]
    K --> L[Log Execution]
    L --> M[Mark Job as Queued]
    M --> D
    D --> N[Wait 1 Minute]
    N --> A
```

---

## Gaps & Improvements Needed

### 1. Configuration-Driven Queue Selection
**Current:** Queue name is read directly from `job.EnvironmentQueue` or defaults to `"default"`.  
**Needed:** Use configuration settings (`AzureQueueContainer`, `AzureQueueContainerOnPremises`) to map environment values to actual queue names.

### 2. Inject Azure Queue Client via DI
**Current:** Queue client is created inline with environment variable.  
**Needed:** Use the Aspire-registered `QueueServiceClient` for proper dependency injection and configuration.

### 3. Robust Error Handling & Retry Logic
**Current:** Basic try-catch with logging.  
**Needed:** Configurable retry policy for queue failures.

### 4. Configurable Polling Interval
**Current:** Hardcoded 1-minute delay.  
**Needed:** Read polling interval from configuration.

### 5. Verbose Logging Option
**Current:** Standard logging only.  
**Needed:** Add verbose logging toggle in configuration.

### 6. Time Zone Handling
**Current:** Uses `DateTime.UtcNow`.  
**Needed:** Option to use local/Pacific time as in reference project.

---

## Architecture Diagram

```mermaid
graph TB
    subgraph "BlazorOrchestrator.Scheduler"
        W[Worker Service]
        DB[(SQL Server<br/>SchedulerDbContext)]
        CFG[appsettings.json]
    end
    
    subgraph "Azure Storage"
        Q1[Azure Queue<br/>default-queue]
        Q2[Azure Queue<br/>azure-queue]
        Q3[Azure Queue<br/>onprem-queue]
    end
    
    subgraph "BlazorOrchestrator.Agent"
        A[Agent Service]
    end
    
    W --> |Read Jobs & Schedules| DB
    W --> |Read Config| CFG
    W --> |Enqueue JobInstance| Q1
    W --> |Enqueue JobInstance| Q2
    W --> |Enqueue JobInstance| Q3
    A --> |Poll Queue| Q1
    A --> |Poll Queue| Q2
    A --> |Poll Queue| Q3
    A --> |Update JobInstance| DB
```

---

## Database Entity Relationships

```mermaid
erDiagram
    Jobs ||--o{ JobSchedule : "has many"
    Jobs ||--o{ JobData : "has many"
    Jobs ||--o{ JobJobGroup : "has many"
    JobSchedule ||--o{ JobInstance : "has many"
    JobInstance ||--o{ JobLogs : "has many"
    JobAgents ||--o{ JobLogs : "has many"
    JobGroups ||--o{ JobJobGroup : "has many"
    
    Jobs {
        int Id PK
        string JobName
        bool Enabled
        bool Queued
        bool InProcess
        bool InError
        string EnvironmentQueue
        string CodeFile
        datetime CreatedDate
        string CreatedBy
        datetime UpdatedDate
        string UpdatedBy
    }
    
    JobSchedule {
        int Id PK
        int JobId FK
        string ScheduleName
        bool Enabled
        bool InProcess
        bool HadError
        datetime LastRun
        int RunEveryHour
        int StartTime
        int StopTime
        bool Monday
        bool Tuesday
        bool Wednesday
        bool Thursday
        bool Friday
        bool Saturday
        bool Sunday
    }
    
    JobInstance {
        int Id PK
        int JobScheduleId FK
        bool InProcess
        bool HasError
        datetime CreatedDate
        string CreatedBy
        datetime UpdatedDate
        string UpdatedBy
    }
    
    JobLogs {
        int Id PK
        int JobInstanceId FK
        int AgentId FK
        string LogData
        bool IsError
        datetime CreatedDate
        string CreatedBy
    }
```

---

## Implementation Plan

### Phase 1: Configuration Improvements

#### Task 1.1: Add Scheduler Settings Class
Create a strongly-typed settings class for scheduler configuration.

```csharp
public class SchedulerSettings
{
    public int PollingIntervalSeconds { get; set; } = 60;
    public bool VerboseLogging { get; set; } = false;
    public string DefaultQueueName { get; set; } = "default-queue";
    public string AzureQueueContainer { get; set; } = "azure-queue";
    public string OnPremisesQueueContainer { get; set; } = "onprem-queue";
    public int StuckJobTimeoutHours { get; set; } = 24;
}
```

#### Task 1.2: Update appsettings.json
```json
{
  "SchedulerSettings": {
    "PollingIntervalSeconds": 60,
    "VerboseLogging": false,
    "DefaultQueueName": "default-queue",
    "AzureQueueContainer": "azure-queue",
    "OnPremisesQueueContainer": "onprem-queue",
    "StuckJobTimeoutHours": 24
  }
}
```

#### Task 1.3: Register Settings in Program.cs
```csharp
builder.Services.Configure<SchedulerSettings>(
    builder.Configuration.GetSection("SchedulerSettings"));
```

---

### Phase 2: Queue Service Improvements

#### Task 2.1: Create Queue Service Interface
```csharp
public interface IJobQueueService
{
    Task<bool> EnqueueJobAsync(int jobInstanceId, int jobId, string queueName);
    string ResolveQueueName(string environmentQueue);
}
```

#### Task 2.2: Implement Queue Service
- Use Aspire's registered `QueueServiceClient`
- Map `EnvironmentQueue` values to actual queue names from configuration
- Include retry logic with configurable policy

```mermaid
flowchart LR
    A[EnvironmentQueue Value] --> B{Value Check}
    B -- "azure" or empty --> C[AzureQueueContainer]
    B -- "onprem" --> D[OnPremisesQueueContainer]
    B -- other --> E[Use as Queue Name]
```

---

### Phase 3: Worker Service Refactoring

#### Task 3.1: Inject Dependencies
- `IJobQueueService`
- `IOptions<SchedulerSettings>`
- `ILogger<Worker>`

#### Task 3.2: Extract Methods
| Method | Responsibility |
|--------|---------------|
| `ProcessScheduledJobsAsync` | Main scheduling loop logic |
| `MarkStuckJobInstancesAsync` | Identify and mark timed-out instances |
| `ShouldScheduleJob` | Determine if job meets scheduling criteria |
| `CreateAndEnqueueJobAsync` | Create instance and send to queue |

#### Task 3.3: Improve Scheduling Logic Flow

```mermaid
sequenceDiagram
    participant W as Worker
    participant DB as Database
    participant QS as QueueService
    participant AQ as Azure Queue
    
    loop Every Polling Interval
        W->>DB: Get Enabled Schedules
        W->>DB: Mark Stuck Instances
        
        loop For Each Schedule
            W->>W: Check Day & Time Window
            W->>DB: Check No Instance In Process
            W->>DB: Get Last Instance
            W->>W: Evaluate Run Interval
            
            alt Should Schedule
                W->>DB: Create JobInstance (InProcess=true)
                W->>DB: Log Creation
                W->>DB: Get Job Details
                W->>QS: Resolve Queue Name
                QS->>AQ: Send Message
                W->>DB: Log Enqueue Success
                W->>DB: Mark Job as Queued
            end
        end
        
        W->>W: Wait Polling Interval
    end
```

---

### Phase 4: Message Format for Agent Compatibility

#### Task 4.1: Define Message Contract
Create a shared message format that the Agent project can deserialize:

```csharp
public class JobQueueMessage
{
    public int JobInstanceId { get; set; }
    public int JobId { get; set; }
    public string QueueName { get; set; }
    public DateTime ScheduledAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}
```

#### Task 4.2: Serialize as JSON
Ensure the message is properly JSON-serialized for cross-service compatibility.

---

### Phase 5: Error Handling & Retry

#### Task 5.1: Retry Policy Configuration
```json
{
  "SchedulerSettings": {
    "RetryCount": 3,
    "RetryDelaySeconds": 5
  }
}
```

#### Task 5.2: Implement Polly Retry Policy
Use Polly for resilient queue operations with exponential backoff.

```mermaid
flowchart TD
    A[Enqueue Job] --> B{Success?}
    B -- Yes --> C[Log Success]
    B -- No --> D{Retry Count < Max?}
    D -- Yes --> E[Wait RetryDelay]
    E --> A
    D -- No --> F[Log Failure]
    F --> G[Mark JobInstance HasError=true]
```

---

## Files to Create/Modify

### New Files
| File | Purpose |
|------|---------|
| `Settings/SchedulerSettings.cs` | Strongly-typed configuration |
| `Services/IJobQueueService.cs` | Queue service interface |
| `Services/JobQueueService.cs` | Queue service implementation |
| `Messages/JobQueueMessage.cs` | Queue message contract |

### Files to Modify
| File | Changes |
|------|---------|
| `Program.cs` | Register settings and services |
| `Worker.cs` | Refactor to use new services and settings |
| `appsettings.json` | Add SchedulerSettings section |
| `appsettings.Development.json` | Add SchedulerSettings section |

---

## Queue Assignment Logic (Your Custom Requirement)

As you mentioned, jobs have a queue ID assigned, and the agent needs to know which queue the job belongs to.

```mermaid
flowchart TD
    A[Job.EnvironmentQueue] --> B{Is Null or Empty?}
    B -- Yes --> C[Use DefaultQueueName<br/>from config]
    B -- No --> D{Equals 'azure'?}
    D -- Yes --> E[Use AzureQueueContainer<br/>from config]
    D -- No --> F{Equals 'onprem'?}
    F -- Yes --> G[Use OnPremisesQueueContainer<br/>from config]
    F -- No --> H[Use EnvironmentQueue<br/>as literal queue name]
    
    C --> I[Final Queue Name]
    E --> I
    G --> I
    H --> I
    
    I --> J[Enqueue with Message:<br/>JobInstanceId, JobId, QueueName]
```

---

## Testing Strategy

### Unit Tests
- [ ] `ShouldScheduleJob` logic with various schedule configurations
- [ ] Queue name resolution logic
- [ ] Message serialization format

### Integration Tests
- [ ] End-to-end scheduling with in-memory database
- [ ] Azure Queue integration with Azurite emulator

### Manual Testing
- [ ] Verify jobs are enqueued to correct queues
- [ ] Verify stuck job detection works after 24 hours
- [ ] Verify polling interval respects configuration

---

## Rollout Plan

1. **Phase 1-2**: Configuration and Queue Service (Low Risk)
2. **Phase 3**: Worker Refactoring (Medium Risk - test thoroughly)
3. **Phase 4**: Message Format (Coordinate with Agent team)
4. **Phase 5**: Retry Logic (Low Risk)

---

## Summary

| Task | Priority | Effort | Risk |
|------|----------|--------|------|
| Configuration Settings Class | High | Low | Low |
| Queue Service with DI | High | Medium | Low |
| Worker Refactoring | High | Medium | Medium |
| Message Format Contract | High | Low | Low |
| Retry Policy | Medium | Low | Low |
| Verbose Logging | Low | Low | Low |
| Time Zone Handling | Low | Low | Low |

---

## Approval

- [ ] Plan reviewed by: _______________
- [ ] Approved for execution: Yes / No
- [ ] Execution start date: _______________

---

*Document created: January 26, 2026*  
*Last updated: January 26, 2026*

# NuGet Execution Feature - Process Diagrams

## Overall System Architecture

```mermaid
graph TB
    subgraph "BlazorOrchestrator.Web"
        UI[JobDetails.razor]
        WP[Program.cs]
    end
    
    subgraph "BlazorDataOrchestrator.Core"
        JM[JobManager]
        JSS[JobStorageService]
        PPS[PackageProcessorService]
        CES[CodeExecutorService]
        DB[(SQL Database)]
    end
    
    subgraph "BlazorOrchestrator.Agent"
        AW[Agent Worker]
        AP[Program.cs]
    end
    
    subgraph "Azure Storage"
        BLOB[(Blob Storage<br/>jobs container)]
        QUEUE[(Queue Storage<br/>job-queue)]
        TABLE[(Table Storage<br/>JobLogs)]
    end
    
    UI --> JM
    WP -.-> JM
    JM --> JSS
    JM --> PPS
    JM --> CES
    JM --> DB
    JSS --> BLOB
    JM --> QUEUE
    JM --> TABLE
    
    AW --> JM
    AP -.-> JM
    PPS --> BLOB
```

---

## Flow 1: Upload NuGet Package

```mermaid
sequenceDiagram
    participant User
    participant UI as JobDetails.razor
    participant JM as JobManager
    participant JSS as JobStorageService
    participant BLOB as Azure Blob<br/>(jobs container)
    participant DB as SQL Database

    User->>UI: Select .nupkg file
    User->>UI: Click "Upload"
    UI->>UI: Read file stream from IBrowserFile
    UI->>JM: UploadJobPackageAsync(jobId, stream, fileName)
    JM->>JSS: UploadPackageAsync(jobId, stream, fileName)
    
    JSS->>JSS: Generate unique name:<br/>{JobId}_{Guid}_{timestamp}.nupkg
    JSS->>BLOB: Upload to "jobs" container
    BLOB-->>JSS: Success
    JSS-->>JM: Return blob name
    
    JM->>DB: Update Job.JobCodeFile = blobName
    DB-->>JM: Success
    JM-->>UI: Return success
    UI-->>User: Show "Upload successful"
```

---

## Flow 2: Run Job Now

```mermaid
sequenceDiagram
    participant User
    participant UI as JobDetails.razor
    participant JM as JobManager
    participant DB as SQL Database
    participant QUEUE as Azure Queue<br/>(job-queue)

    User->>UI: Click "Run Job Now"
    UI->>JM: RunJobNowAsync(jobId)
    
    JM->>DB: Check for existing JobSchedule
    alt No schedule exists
        JM->>DB: Create default JobSchedule
    end
    
    JM->>DB: Create JobInstance<br/>(InProcess=false)
    DB-->>JM: Return JobInstanceId
    
    JM->>JM: Create JobQueueMessage<br/>{JobInstanceId, JobId, QueuedAt}
    JM->>QUEUE: Send message (Base64 encoded)
    QUEUE-->>JM: Message queued
    
    JM-->>UI: Return JobInstanceId
    UI->>UI: Refresh logs
    UI-->>User: Show "Job queued"
```

---

## Flow 3: Agent Processing

```mermaid
sequenceDiagram
    participant QUEUE as Azure Queue<br/>(job-queue)
    participant AW as Agent Worker
    participant JM as JobManager
    participant PPS as PackageProcessorService
    participant BLOB as Azure Blob<br/>(jobs container)
    participant CES as CodeExecutorService
    participant DB as SQL Database
    participant TABLE as Azure Table<br/>(JobLogs)

    loop Every 5 seconds
        AW->>QUEUE: ReceiveMessageAsync()
        alt Message exists
            QUEUE-->>AW: JobQueueMessage
            AW->>AW: Deserialize message
            AW->>JM: ProcessJobInstanceAsync(jobInstanceId)
            
            JM->>DB: Get JobInstance + JobSchedule + Job
            DB-->>JM: Job data (includes JobCodeFile)
            
            JM->>JM: Mark JobInstance.InProcess = true
            JM->>DB: Save changes
            
            JM->>PPS: DownloadAndExtractPackageAsync(blobName, tempPath)
            PPS->>BLOB: Download package
            BLOB-->>PPS: Package bytes
            PPS->>PPS: Extract to temp folder
            PPS-->>JM: Extracted path
            
            JM->>PPS: ValidateNuSpecAsync(extractedPath)
            PPS->>PPS: Check .nuspec exists
            PPS->>PPS: Verify folder structure
            
            alt Invalid package
                PPS-->>JM: Validation failed
                JM->>TABLE: Log error
                JM->>DB: Mark HasError = true
            else Valid package
                PPS-->>JM: Validation passed
                
                JM->>PPS: GetConfigurationAsync(extractedPath)
                PPS->>PPS: Read configuration.json
                PPS-->>JM: {SelectedLanguage: "CSharp" | "Python"}
                
                JM->>CES: ExecuteAsync(path, context)
                
                alt SelectedLanguage = CSharp
                    CES->>CES: Load main.cs
                    CES->>CES: Compile with CSScript
                    CES->>CES: Call ExecuteJob()
                else SelectedLanguage = Python
                    CES->>CES: Load main.py
                    CES->>CES: Execute with CSSnakes/subprocess
                    CES->>CES: Call execute_job()
                end
                
                CES-->>JM: Execution logs
                JM->>TABLE: Log results
                JM->>DB: Mark InProcess = false
            end
            
            JM-->>AW: Processing complete
            AW->>QUEUE: DeleteMessageAsync()
        else No message
            QUEUE-->>AW: null
            AW->>AW: Wait 5 seconds
        end
    end
```

---

## Flow 4: Package Validation Detail

```mermaid
flowchart TD
    A[Receive Package Path] --> B{.nuspec file exists?}
    B -->|No| C[Log Error: Missing .nuspec]
    B -->|Yes| D{Check folder structure}
    
    D --> E{contentFiles/any/any exists?}
    E -->|No| F[Log Error: Invalid structure]
    E -->|Yes| G{CodeCSharp OR CodePython exists?}
    
    G -->|No| H[Log Error: No code folder]
    G -->|Yes| I{configuration.json exists?}
    
    I -->|No| J[Use default: CSharp]
    I -->|Yes| K[Read SelectedLanguage]
    
    J --> L[Validation Passed]
    K --> L
    
    C --> M[Return: Invalid]
    F --> M
    H --> M
    L --> N[Return: Valid]
```

---

## Flow 5: Code Execution Detail

```mermaid
flowchart TD
    A[Start Execution] --> B{SelectedLanguage?}
    
    B -->|CSharp| C[Load CodeCSharp/main.cs]
    B -->|Python| D[Load CodePython/main.py]
    
    C --> E[Find additional .dll references]
    E --> F[Compile with CSScript]
    F --> G[Find BlazorDataOrchestratorJob class]
    G --> H{ExecuteJob method exists?}
    
    H -->|No| I[Log Error: Missing entry point]
    H -->|Yes| J[Call ExecuteJob with parameters]
    
    J --> K[appSettings, jobAgentId, jobId,<br/>jobInstanceId, jobScheduleId]
    K --> L[Capture return List&lt;string&gt;]
    
    D --> M[Check requirements.txt]
    M --> N[Execute with CSSnakes/subprocess]
    N --> O[Call execute_job function]
    O --> P[Capture return list]
    
    L --> Q[Log results to Table Storage]
    P --> Q
    I --> R[Mark job as error]
    Q --> S[End Execution]
    R --> S
```

---

## Data Model Relationships

```mermaid
erDiagram
    Job ||--o{ JobSchedule : has
    Job ||--o{ JobDatum : has
    Job }o--|| JobOrganization : belongs_to
    JobSchedule ||--o{ JobInstance : creates
    
    Job {
        int Id PK
        int JobOrganizationId FK
        string JobName
        string JobCodeFile "Blob name in Azure Storage"
        bool JobEnabled
        bool JobQueued
        bool JobInProcess
        bool JobInError
    }
    
    JobSchedule {
        int Id PK
        int JobId FK
        string ScheduleName
        bool Enabled
        bool InProcess
    }
    
    JobInstance {
        int Id PK
        int JobScheduleId FK
        bool InProcess
        bool HasError
        string AgentId
    }
    
    JobQueueMessage {
        int JobInstanceId
        int JobId
        datetime QueuedAt
    }
```

---

## Component Dependencies

```mermaid
graph LR
    subgraph "NuGet Packages"
        AZB[Azure.Storage.Blobs]
        AZQ[Azure.Storage.Queues]
        AZT[Azure.Data.Tables]
        CSS[CSScriptLib]
        CSN[CSnakes]
    end
    
    subgraph "Core Services"
        JSS[JobStorageService]
        PPS[PackageProcessorService]
        CES[CodeExecutorService]
        JM[JobManager]
    end
    
    JSS --> AZB
    PPS --> AZB
    JM --> AZQ
    JM --> AZT
    CES --> CSS
    CES --> CSN
```

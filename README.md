# NOTE: This project is currently *in progress*

# BlazorDataOrchestrator
<img width="426" height="176" alt="BlazorDataOrchestratorLogo" src="https://github.com/user-attachments/assets/9df86186-193a-4a48-a2ba-e751abbf21eb" />

A Microsoft Aspire .NET 9 solution that demonstrates a distributed application architecture with Blazor Server Web App and containerized services.

<img width="972" height="393" alt="image" src="https://github.com/user-attachments/assets/12767a72-8928-463c-b278-962cfc2e3d2b" />

<img width="833" height="505" alt="image" src="https://github.com/user-attachments/assets/8cc7af0d-e7a2-45d3-abbb-509a3c8fff75" />

## Architecture

This solution implements the following components:

### Components

1. **BlazorOrchistrator.AppHost** - Aspire orchestration host that manages all services
2. **BlazorOrchistrator.Web** - Blazor Server Web Application with interactive components
3. **BlazorOrchistrator.Scheduler** - Background service for scheduled tasks
4. **BlazorOrchistrator.Agent** - Containerized worker service that can run with multiple replicas

### Aspire Services Configured

- **Azure Storage** - Blob and Queue storage integration
- **SQL Server** - Database services with Entity Framework Core
- **Container Orchestration** - Agent service configured for 2 replicas

## Prerequisites

- .NET 10.0 SDK
- Docker Desktop (for container services)
- Aspire workload: `dotnet workload install aspire`

## Project Structure

```
src/
├── BlazorOrchistrator.AppHost/        # Aspire orchestration host
├── BlazorOrchistrator.Web/            # Blazor Server web application
├── BlazorOrchistrator.Scheduler/      # Background scheduler service
└── BlazorOrchistrator.Agent/          # Containerized agent service
    └── Dockerfile                     # Container configuration
```

## Configuration

The solution is configured to:

- Use in-memory databases in development mode
- Switch to SQL Server and Azure Storage when running with Aspire orchestration
- Support multiple replicas of the Agent service
- Provide proper service discovery and health monitoring

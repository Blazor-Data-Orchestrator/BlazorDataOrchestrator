## This project is in progress

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

- .NET 9.0 SDK
- Docker Desktop (for container services)
- Aspire workload: `dotnet workload install aspire`

## Running the Solution

### Development Mode (Individual Services)

For development without full Aspire orchestration:

1. **Run the Blazor Web App:**
   ```bash
   cd src/BlazorOrchistrator.Web
   dotnet run
   ```
   Navigate to `http://localhost:5287`

2. **Run the Scheduler Service:**
   ```bash
   cd src/BlazorOrchistrator.Scheduler
   dotnet run
   ```

3. **Run the Agent Service:**
   ```bash
   cd src/BlazorOrchistrator.Agent
   dotnet run
   ```

### Full Aspire Orchestration

To run the complete orchestrated solution:

```bash
cd src/BlazorOrchistrator.AppHost
dotnet run
```

This will start all services and the Aspire dashboard.

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

## Development Notes

- The solution uses conditional service registration based on environment
- In development mode, Azure Storage services are mocked/disabled
- Entity Framework contexts are properly scoped to avoid DI conflicts
- All services include comprehensive logging

## Building and Testing

```bash
# Build the entire solution
dotnet build

# Run tests (if any)
dotnet test

# Restore packages
dotnet restore
```

## Container Support

The Agent service includes a Dockerfile for containerization. To build and run:

```bash
cd src/BlazorOrchistrator.Agent
docker build -t blazor-orchestrator-agent .
docker run blazor-orchestrator-agent
```

# Aspire Configuration Summary

This document outlines the .NET Aspire setup for the BlazorOrchestrator solution, configured following best practices similar to the LocalInternalAIChatBot project.

## Projects Structure

### 1. BlazorOrchistrator.AppHost
- **Purpose**: Orchestrates all services and infrastructure
- **Key Features**:
  - SQL Server container with persistent volume
  - Azure Storage (Blobs and Queues) emulation
  - Service discovery and health monitoring
  - Environment variable configuration

### 2. BlazorOrchistrator.ServiceDefaults
- **Purpose**: Shared Aspire services and telemetry configuration
- **Features**:
  - OpenTelemetry tracing and metrics
  - Health checks
  - Service discovery
  - HTTP resilience patterns
  - Standardized logging

### 3. Application Projects
- **BlazorOrchistrator.Web**: Blazor Server application
- **BlazorOrchistrator.Scheduler**: Background worker service
- **BlazorOrchistrator.Agent**: Background worker service

## Key Configuration Changes

### SQL Server Setup
```csharp
var sqlPassword = builder.AddParameter("sqlPassword", secret: true);
var sql = builder.AddSqlServer("sql", password: sqlPassword, port: 1433)
    .WithDataVolume()
    .AddDatabase("database");
```

### Service Dependencies
All services are configured with:
- SQL Server database connection
- Azure Storage (queues and blobs)
- Health monitoring
- Service discovery
- Telemetry collection

### Security
- SQL Server password stored in User Secrets
- Health check endpoints only exposed in development
- OpenTelemetry configured with OTLP exporter support

## Running the Application

1. **Set SQL Password** (if not already done):
   ```bash
   cd src/BlazorOrchistrator.AppHost
   dotnet user-secrets set "Parameters:sqlPassword" "YourStr0ng!Passw0rd"
   ```

2. **Run the Application**:
   ```bash
   dotnet run --project src/BlazorOrchistrator.AppHost
   ```

3. **Access Services**:
   - Aspire Dashboard: Usually at `https://localhost:17000`
   - Blazor Web App: Port assigned by Aspire
   - Health Checks: `/health` and `/alive` endpoints

## Infrastructure Components

### SQL Server
- Runs in a container with persistent storage
- Accessible to all application services
- Connection string managed by Aspire

### Azure Storage Emulator
- Azurite provides local blob and queue storage
- Compatible with Azure Storage APIs
- No Azure subscription required for development

### Monitoring and Observability
- OpenTelemetry for distributed tracing
- Health checks for service monitoring
- Metrics collection for performance monitoring
- Aspire Dashboard for real-time insights

## Benefits of This Setup

1. **Local Development**: Everything runs locally without external dependencies
2. **Production Ready**: Same code works in production with real Azure services
3. **Observability**: Built-in telemetry and monitoring
4. **Scalability**: Easy to add replicas and new services
5. **Security**: Proper secret management and secure defaults
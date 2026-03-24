# ---- Runtime base image with Python ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# Install Python 3 and pip for Python job execution
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        python3 \
        python3-pip \
        python3-venv && \
    ln -sf /usr/bin/python3 /usr/bin/python && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# ---- Build stage ----
# Build context is expected to be the src/ directory so that
# project references (Core, ServiceDefaults) can be restored.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files for restore (layer caching)
COPY BlazorOrchestrator.Agent/BlazorOrchestrator.Agent.csproj BlazorOrchestrator.Agent/
COPY BlazorDataOrchestrator.Core/BlazorDataOrchestrator.Core.csproj BlazorDataOrchestrator.Core/
COPY BlazorOrchestrator.ServiceDefaults/BlazorOrchestrator.ServiceDefaults.csproj BlazorOrchestrator.ServiceDefaults/
RUN dotnet restore "BlazorOrchestrator.Agent/BlazorOrchestrator.Agent.csproj"

# Copy everything and build
COPY BlazorOrchestrator.Agent/ BlazorOrchestrator.Agent/
COPY BlazorDataOrchestrator.Core/ BlazorDataOrchestrator.Core/
COPY BlazorOrchestrator.ServiceDefaults/ BlazorOrchestrator.ServiceDefaults/
RUN dotnet build "BlazorOrchestrator.Agent/BlazorOrchestrator.Agent.csproj" -c Release -o /app/build

# ---- Publish stage ----
FROM build AS publish
RUN dotnet publish "BlazorOrchestrator.Agent/BlazorOrchestrator.Agent.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ---- Final stage ----
FROM base AS final
WORKDIR /app

# Pre-install common Python packages used by jobs
COPY BlazorOrchestrator.Agent/requirements.txt /tmp/requirements.txt
RUN pip3 install --no-cache-dir --break-system-packages \
    -r /tmp/requirements.txt || true && \
    rm /tmp/requirements.txt

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BlazorOrchestrator.Agent.dll"]
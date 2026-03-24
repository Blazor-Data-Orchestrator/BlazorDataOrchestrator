FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
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

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["BlazorOrchestrator.Agent.csproj", "."]
RUN dotnet restore "./BlazorOrchestrator.Agent.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "BlazorOrchestrator.Agent.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BlazorOrchestrator.Agent.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# Pre-install common Python packages used by jobs
COPY requirements.txt /tmp/requirements.txt
RUN pip3 install --no-cache-dir --break-system-packages \
    -r /tmp/requirements.txt || true && \
    rm /tmp/requirements.txt

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BlazorOrchestrator.Agent.dll"]
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

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
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BlazorOrchestrator.Agent.dll"]
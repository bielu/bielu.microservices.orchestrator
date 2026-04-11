# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["Bielu.Microservices.Orchestrator.Examples.Worker/Bielu.Microservices.Orchestrator.Examples.Worker.csproj", "Bielu.Microservices.Orchestrator.Examples.Worker/"]
RUN dotnet restore "Bielu.Microservices.Orchestrator.Examples.Worker/Bielu.Microservices.Orchestrator.Examples.Worker.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/Bielu.Microservices.Orchestrator.Examples.Worker"
RUN dotnet build "Bielu.Microservices.Orchestrator.Examples.Worker.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Bielu.Microservices.Orchestrator.Examples.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Environment variables for configuration
ENV WORKER_ID=worker-1
ENV ENVIRONMENT=production
ENV DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "Bielu.Microservices.Orchestrator.Examples.Worker.dll"]

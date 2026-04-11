FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /App

# Copy everything
COPY . ./
RUN ls -R ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish ./Bielu.Microservices.Orchestrator.Examples.Worker/Bielu.Microservices.Orchestrator.Examples.Worker.csproj -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /App
COPY --from=build /App/out .
ENTRYPOINT ["dotnet", "Bielu.Microservices.Orchestrator.Examples.Worker.dll"]
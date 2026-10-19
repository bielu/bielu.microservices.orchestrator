# Getting Started

## 1. Install the core package and a runtime provider

```bash
# Core + Docker (most common)
dotnet add package Bielu.Microservices.Orchestrator
dotnet add package Bielu.Microservices.Orchestrator.Docker

# — or Podman —
dotnet add package Bielu.Microservices.Orchestrator.Podman

# — or Kubernetes —
dotnet add package Bielu.Microservices.Orchestrator.Kubernetes

# — or containerd —
dotnet add package Bielu.Microservices.Orchestrator.Containerd
```

All packages target **.NET 10** and share a single version.

## 2. Register the orchestrator in DI

```csharp
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Docker.Extensions;

builder.Services.AddMicroservicesOrchestrator(orchestrator =>
{
    orchestrator.AddDocker(); // uses the default Docker socket
});
```

Each provider exposes an options delegate for custom configuration:

```csharp
orchestrator.AddDocker(options =>
{
    // Linux / macOS default: unix:///var/run/docker.sock
    // Windows default:       npipe://./pipe/docker_engine
    options.Endpoint = "tcp://remote-host:2376";
});
```

## 3. Inject `IContainerOrchestrator` and use it

```csharp
using Bielu.Microservices.Orchestrator.Abstractions;

public class MyService(IContainerOrchestrator orchestrator)
{
    public async Task RunAsync()
    {
        // Check the runtime is reachable
        var available = await orchestrator.IsAvailableAsync();

        // List running containers
        var containers = await orchestrator.Containers.ListAsync();
    }
}
```

<xref:Bielu.Microservices.Orchestrator.Abstractions.IContainerOrchestrator> aggregates four managers:

| Member | Description |
|--------|-------------|
| `Containers` | <xref:Bielu.Microservices.Orchestrator.Abstractions.IContainerManager> — container lifecycle |
| `Images` | <xref:Bielu.Microservices.Orchestrator.Abstractions.IImageManager> — pull, tag, remove images |
| `Networks` | <xref:Bielu.Microservices.Orchestrator.Abstractions.INetworkManager> — networks and attachments |
| `Volumes` | <xref:Bielu.Microservices.Orchestrator.Abstractions.IVolumeManager> — volumes |
| `ProviderName` | Runtime name (e.g. `"Docker"`, `"Podman"`) |
| `IsAvailableAsync()` | Whether the runtime is reachable |

## Builder options

| Method | Description |
|--------|-------------|
| `WithManagedContainersOnly(bool)` | Only list and manage containers the orchestrator created (default: `true`). Pass `false` to see every container on the host. |
| `WithDefaultProvider(string)` | Sets the default runtime provider by name (e.g. `"Docker"`, `"Containerd"`). |
| `WithStateTracking()` | Persists desired state and reconciles it on startup — see [State Tracking & Recovery](state-tracking.md). |
| `AddOpenTelemetryInstrumentation()` | Tracing and metrics decorators — see [Observability](observability.md). |

## Next steps

- [Runtime Providers](providers.md)
- [Container Lifecycle](container-lifecycle.md)
- A complete ASP.NET Core sample lives in [`src/Bielu.Microservices.Orchestrator.Examples.Api`](https://github.com/bielu/bielu.microservices.orchestrator/tree/main/src/Bielu.Microservices.Orchestrator.Examples.Api), with a .NET Aspire AppHost alongside it.

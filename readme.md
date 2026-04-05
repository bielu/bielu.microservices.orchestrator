# Bielu.Microservices.Orchestrator

A .NET library for managing container runtimes (Docker, Podman, containerd, and Kubernetes) through a unified abstraction layer. Write your container-management logic once and swap the underlying runtime with a single line of configuration.

## Features

- **Unified API** — single set of interfaces (`IContainerManager`, `IImageManager`, `INetworkManager`, `IVolumeManager`) shared across all runtimes
- **Docker** — full Docker Engine API integration via Docker.DotNet
- **Podman** — Docker-compatible API, zero extra code required
- **containerd** — gRPC-based containerd runtime integration
- **Kubernetes** — pod/deployment management via the official Kubernetes client
- **OpenTelemetry tracing** — automatic `Activity` spans around every operation
- **Health checks** — `IHealthCheck` that verifies the container runtime is reachable
- **Dependency injection** — first-class `Microsoft.Extensions.DependencyInjection` support
- **Aspire integration** — example and test projects using .NET Aspire

## Packages

| Package | Description |
|---------|-------------|
| `Bielu.Microservices.Orchestrator` | Core abstractions, models, and builder |
| `Bielu.Microservices.Orchestrator.Docker` | Docker runtime provider |
| `Bielu.Microservices.Orchestrator.Podman` | Podman runtime provider |
| `Bielu.Microservices.Orchestrator.Containerd` | containerd runtime provider |
| `Bielu.Microservices.Orchestrator.Kubernetes` | Kubernetes runtime provider |
| `Bielu.Microservices.Orchestrator.OpenTelemetry` | OpenTelemetry tracing decorators |
| `Bielu.Microservices.Orchestrator.HealthChecks` | ASP.NET Core health-check integration |

---

## Getting Started

### 1. Install the core package and a runtime provider

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

### 2. Register the orchestrator in DI

```csharp
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Docker.Extensions;

builder.Services.AddMicroservicesOrchestrator(orchestrator =>
{
    orchestrator.AddDocker(); // uses default Docker socket
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

### 3. Inject `IContainerOrchestrator` and use it

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

---

## Container Lifecycle

The `IContainerManager` interface (exposed via `orchestrator.Containers`) provides the full create → start → stop → remove workflow.

### Create a container

```csharp
using Bielu.Microservices.Orchestrator.Models;

var containerId = await orchestrator.Containers.CreateAsync(new CreateContainerRequest
{
    Image  = "nginx:latest",
    Name   = "my-nginx",
    Ports  = [new PortMapping { ContainerPort = 80, HostPort = 8080 }],
    EnvironmentVariables = new Dictionary<string, string>
    {
        ["NGINX_HOST"] = "localhost"
    },
    Labels = new Dictionary<string, string>
    {
        ["app"] = "demo"
    }
});
```

### Start a container

```csharp
await orchestrator.Containers.StartAsync(containerId);
```

### Stop a container

```csharp
// Graceful stop (waits up to 30 s for the process to exit)
await orchestrator.Containers.StopAsync(containerId, timeout: TimeSpan.FromSeconds(30));
```

### Get logs

```csharp
var logs = await orchestrator.Containers.GetLogsAsync(containerId, stdout: true, stderr: true);
```

### Remove a container

```csharp
// Remove a stopped container
await orchestrator.Containers.RemoveAsync(containerId);

// Force-remove a running container
await orchestrator.Containers.RemoveAsync(containerId, force: true);
```

### Inspect a container

```csharp
var info = await orchestrator.Containers.GetAsync(containerId);
// info.Id, info.Name, info.Image, info.State, ...
```

---

## Image Management

```csharp
// Pull an image
await orchestrator.Images.PullAsync(new PullImageRequest
{
    Image = "nginx",
    Tag   = "latest"
});

// List images
var images = await orchestrator.Images.ListAsync();

// Tag an image
await orchestrator.Images.TagAsync(imageId, "myregistry/nginx", "v2");

// Remove an image
await orchestrator.Images.RemoveAsync(imageId, force: true);
```

## Network Management

```csharp
// Create a bridge network
var networkId = await orchestrator.Networks.CreateAsync("my-network", driver: "bridge");

// Connect / disconnect a container
await orchestrator.Networks.ConnectAsync(networkId, containerId);
await orchestrator.Networks.DisconnectAsync(networkId, containerId);

// List and remove
var networks = await orchestrator.Networks.ListAsync();
await orchestrator.Networks.RemoveAsync(networkId);
```

## Volume Management

```csharp
// Create a volume
var volume = await orchestrator.Volumes.CreateAsync("my-volume", driver: "local");

// List and remove
var volumes = await orchestrator.Volumes.ListAsync();
await orchestrator.Volumes.RemoveAsync("my-volume", force: true);
```

---

## OpenTelemetry Tracing

Install the tracing package:

```bash
dotnet add package Bielu.Microservices.Orchestrator.OpenTelemetry
```

### Wire up tracing

`AddOpenTelemetryInstrumentation()` wraps every registered manager interface with a decorator that opens an `Activity` span per operation. Call it **after** registering a provider:

```csharp
using Bielu.Microservices.Orchestrator.OpenTelemetry.Extensions;

builder.Services.AddMicroservicesOrchestrator(orchestrator =>
{
    orchestrator
        .AddDocker()
        .AddOpenTelemetryInstrumentation(); // must come after the provider
});
```

Then register the library's `ActivitySource` with the OpenTelemetry SDK so spans reach your exporter:

```csharp
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddOrchestratorInstrumentation()   // orchestrator spans
        .AddAspNetCoreInstrumentation()     // HTTP request spans
        .AddConsoleExporter());             // or AddOtlpExporter() for production
```

Every container, image, network, and volume operation will now emit a span with semantic attributes such as `container.id`, `container.image`, `network.driver`, and `volume.name`.

---

## Health Checks

Install the health-checks package:

```bash
dotnet add package Bielu.Microservices.Orchestrator.HealthChecks
```

### Register the health check

`AddContainerRuntimeHealthCheck()` adds an `IHealthCheck` that calls `IContainerOrchestrator.IsAvailableAsync()`:

```csharp
using Bielu.Microservices.Orchestrator.HealthChecks.Extensions;

builder.Services.AddHealthChecks()
    .AddContainerRuntimeHealthCheck(
        name: "container-runtime",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready", "runtime"]);
```

### Map health-check endpoints

```csharp
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// Liveness — always healthy if the process is running
app.MapHealthChecks("/health");

// Readiness — healthy only when the container runtime is reachable
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});
```

The check returns:

| Runtime reachable | Result |
|---|---|
| ✅ Yes | `Healthy` — *"Container runtime 'Docker' is available."* |
| ❌ No | `Unhealthy` — *"Container runtime 'Docker' is not reachable."* |
| 💥 Exception | `Unhealthy` — exception details attached |

---

## Switching Runtime Providers

Because every provider implements the same interfaces, switching runtimes is a one-line change. The rest of your code stays exactly the same:

```csharp
builder.Services.AddMicroservicesOrchestrator(orchestrator =>
{
    // Pick one:
    orchestrator.AddDocker();
    // orchestrator.AddPodman(o => o.Endpoint = "unix:///run/podman/podman.sock");
    // orchestrator.AddKubernetes(o => o.Namespace = "my-namespace");
    // orchestrator.AddContainerd(o => o.Endpoint = "http://localhost:1234");

    orchestrator.AddOpenTelemetryInstrumentation(); // optional, works with any provider
});
```

---

## Full ASP.NET Core Example

A complete working example lives in [`src/Bielu.Microservices.Orchestrator.Examples.Api`](src/Bielu.Microservices.Orchestrator.Examples.Api/Program.cs). Here is the condensed version:

```csharp
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Docker.Extensions;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.HealthChecks.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// 1. Register orchestrator with Docker + tracing
builder.Services.AddMicroservicesOrchestrator(o => o
    .AddDocker()
    .AddOpenTelemetryInstrumentation());

// 2. Configure OpenTelemetry exporter
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddOrchestratorInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

// 3. Health checks
builder.Services.AddHealthChecks()
    .AddContainerRuntimeHealthCheck(
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready", "runtime"]);

var app = builder.Build();

// Health endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});

// Container CRUD
app.MapGet("/api/containers", async (IContainerOrchestrator o, bool all = false) =>
    Results.Ok(await o.Containers.ListAsync(all)));

app.MapPost("/api/containers", async (CreateContainerRequest req, IContainerOrchestrator o) =>
{
    var id = await o.Containers.CreateAsync(req);
    return Results.Created($"/api/containers/{id}", new { Id = id });
});

app.MapPost("/api/containers/{id}/start", async (string id, IContainerOrchestrator o) =>
{
    await o.Containers.StartAsync(id);
    return Results.Ok(new { Message = $"Container {id} started." });
});

app.MapPost("/api/containers/{id}/stop", async (string id, IContainerOrchestrator o, int? timeoutSeconds) =>
{
    var timeout = timeoutSeconds.HasValue ? TimeSpan.FromSeconds(timeoutSeconds.Value) : (TimeSpan?)null;
    await o.Containers.StopAsync(id, timeout);
    return Results.Ok(new { Message = $"Container {id} stopped." });
});

app.MapGet("/api/containers/{id}/logs", async (string id, IContainerOrchestrator o) =>
    Results.Ok(new { Logs = await o.Containers.GetLogsAsync(id) }));

app.MapDelete("/api/containers/{id}", async (string id, IContainerOrchestrator o, bool force = false) =>
{
    await o.Containers.RemoveAsync(id, force);
    return Results.NoContent();
});

// Provider info
app.MapGet("/api/provider", async (IContainerOrchestrator o) =>
    Results.Ok(new { o.ProviderName, IsAvailable = await o.IsAvailableAsync() }));

app.Run();
```

---

## API Reference

### `IContainerOrchestrator`

| Member | Description |
|--------|-------------|
| `Containers` | Access to `IContainerManager` |
| `Images` | Access to `IImageManager` |
| `Networks` | Access to `INetworkManager` |
| `Volumes` | Access to `IVolumeManager` |
| `ProviderName` | Runtime name (e.g., `"Docker"`, `"Podman"`) |
| `IsAvailableAsync()` | Check whether the runtime is reachable |

### `IContainerManager`

| Method | Description |
|--------|-------------|
| `ListAsync(bool all)` | List containers (running only, or all) |
| `GetAsync(string id)` | Inspect a single container |
| `CreateAsync(CreateContainerRequest)` | Create a container and return its ID |
| `StartAsync(string id)` | Start a container |
| `StopAsync(string id, TimeSpan? timeout)` | Gracefully stop a container |
| `RemoveAsync(string id, bool force)` | Remove a container |
| `GetLogsAsync(string id, bool stdout, bool stderr)` | Retrieve container logs |

### `IImageManager`

| Method | Description |
|--------|-------------|
| `ListAsync()` | List images |
| `GetAsync(string id)` | Inspect a single image |
| `PullAsync(PullImageRequest)` | Pull an image from a registry |
| `RemoveAsync(string id, bool force)` | Remove an image |
| `TagAsync(string id, string repo, string tag)` | Tag an image |

### `INetworkManager`

| Method | Description |
|--------|-------------|
| `ListAsync()` | List networks |
| `CreateAsync(string name, string driver)` | Create a network |
| `RemoveAsync(string id)` | Remove a network |
| `ConnectAsync(string networkId, string containerId)` | Connect a container |
| `DisconnectAsync(string networkId, string containerId)` | Disconnect a container |

### `IVolumeManager`

| Method | Description |
|--------|-------------|
| `ListAsync()` | List volumes |
| `CreateAsync(string name, string? driver)` | Create a volume |
| `RemoveAsync(string name, bool force)` | Remove a volume |

---

## License

MIT — see [LICENSE.md](LICENSE.md) for details.

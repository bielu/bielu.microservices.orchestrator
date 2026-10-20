# Container Lifecycle

<xref:Bielu.Microservices.Orchestrator.Abstractions.IContainerManager> (exposed as `orchestrator.Containers`) provides the full create → start → stop → remove workflow.

## Create a container

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

### Fluent request builder

The same request can be built with the extension methods in <xref:Bielu.Microservices.Orchestrator.Extensions.CreateContainerRequestExtensions>:

```csharp
var request = new CreateContainerRequest()
    .WithImage("nginx:latest")
    .WithName("my-nginx")
    .WithPort(80, 8080)
    .WithEnvironmentVariable("NGINX_HOST", "localhost")
    .WithLabel("app", "demo")
    .WithVolume("/srv/www", "/usr/share/nginx/html");

var containerId = await orchestrator.Containers.CreateAsync(request);
```

| Method | Description |
|---|---|
| `WithImage` / `WithName` / `WithCommand` | Image, name and command |
| `WithEnvironmentVariable` / `WithLabel` | Environment variables and labels |
| `WithPort` | Port mapping; optionally bind to a host IP such as `"127.0.0.1"` |
| `WithVolume` | Docker-style `host:container[:ro\|rw]`, a host path/container path pair, or a `VolumeMount` |
| `WithLocalBoundVolume` | Local-driver volume bound to a host directory, created on demand |
| `WithReplicas` | Number of instances to create |
| `WithAutoRemove` | Remove the container when it exits |

## Create multiple instances (replicas)

Set `Replicas` to create multiple container instances from the same configuration. Each instance is named with a numeric suffix (e.g. `my-nginx-0`, `my-nginx-1`) and labeled with `orchestrator.group` for grouping.

```csharp
var firstContainerId = await orchestrator.Containers.CreateAsync(new CreateContainerRequest
{
    Image    = "nginx:latest",
    Name     = "my-nginx",
    Replicas = 3    // creates my-nginx-0, my-nginx-1, my-nginx-2
});
```

## Start, stop and inspect

```csharp
await orchestrator.Containers.StartAsync(containerId);

// Graceful stop (waits up to 30 s for the process to exit)
await orchestrator.Containers.StopAsync(containerId, timeout: TimeSpan.FromSeconds(30));

var info = await orchestrator.Containers.GetAsync(containerId);
// info.Id, info.Name, info.Image, info.State, info.Ports, ...
```

## Logs

```csharp
var logs = await orchestrator.Containers.GetLogsAsync(containerId, stdout: true, stderr: true);
```

## Remove

```csharp
// Remove a stopped container
await orchestrator.Containers.RemoveAsync(containerId);

// Force-remove a running container
await orchestrator.Containers.RemoveAsync(containerId, force: true);
```

## Method reference

| Method | Description |
|--------|-------------|
| `ListAsync(bool all)` | List containers (running only, or all) |
| `GetAsync(string id)` | Inspect a single container |
| `CreateAsync(CreateContainerRequest)` | Create a container (or several with `Replicas`) and return its ID |
| `StartAsync(string id)` | Start a container |
| `StopAsync(string id, TimeSpan? timeout)` | Gracefully stop a container |
| `RemoveAsync(string id, bool force)` | Remove a container |
| `GetLogsAsync(string id, bool stdout, bool stderr)` | Retrieve container logs |
| `ScaleAsync(string id, int replicas)` | Scale instances (runtime-dependent; may throw `NotSupportedException`) |

By default the orchestrator only lists and manages containers it created; call `WithManagedContainersOnly(false)` on the builder to work with every container on the host.

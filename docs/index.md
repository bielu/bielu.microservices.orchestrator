---
_layout: landing
---

# Bielu Microservices Orchestrator

A .NET library for managing container runtimes — **Docker**, **Podman**, **containerd** and **Kubernetes** — through one unified abstraction. Write your container-management logic once and swap the runtime with a single line of configuration.

```bash
dotnet add package Bielu.Microservices.Orchestrator
dotnet add package Bielu.Microservices.Orchestrator.Docker
```

```csharp
builder.Services.AddMicroservicesOrchestrator(orchestrator => orchestrator.AddDocker());

// anywhere in your app
public class MyService(IContainerOrchestrator orchestrator)
{
    public Task<string> RunNginxAsync() =>
        orchestrator.Containers.CreateAsync(new CreateContainerRequest()
            .WithImage("nginx:latest")
            .WithName("my-nginx")
            .WithPort(80, 8080));
}
```

<div class="landing-cards">
<div class="landing-card">

## Get started

Install a provider, register the orchestrator and run your first container.

[Getting Started →](articles/getting-started.md)

</div>
<div class="landing-card">

## Runtime providers

Docker, Podman, containerd and Kubernetes behind the same interfaces.

[Runtime Providers →](articles/providers.md)

</div>
<div class="landing-card">

## Production ready

State tracking with recovery, image updates, OpenTelemetry tracing and metrics, health checks.

[Observability →](articles/observability.md)

</div>
</div>

## Packages

| Package | Description |
|---------|-------------|
| `Bielu.Microservices.Orchestrator` | Core abstractions, models and builder |
| `Bielu.Microservices.Orchestrator.Docker` | Docker runtime provider |
| `Bielu.Microservices.Orchestrator.Podman` | Podman runtime provider |
| `Bielu.Microservices.Orchestrator.Containerd` | containerd runtime provider |
| `Bielu.Microservices.Orchestrator.Kubernetes` | Kubernetes runtime provider |
| `Bielu.Microservices.Orchestrator.OpenTelemetry` | OpenTelemetry tracing and metrics |
| `Bielu.Microservices.Orchestrator.HealthChecks` | ASP.NET Core health-check integration |
| `Bielu.Microservices.Orchestrator.Storage.EfCore` | EF Core instance store |
| `Bielu.Microservices.Orchestrator.Storage.File` | File-based (JSON) instance store |

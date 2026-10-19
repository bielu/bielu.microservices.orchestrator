# Health Checks

```bash
dotnet add package Bielu.Microservices.Orchestrator.HealthChecks
```

## Register the health check

`AddContainerRuntimeHealthCheck()` adds an `IHealthCheck` that calls `IContainerOrchestrator.IsAvailableAsync()`:

```csharp
using Bielu.Microservices.Orchestrator.HealthChecks.Extensions;

builder.Services.AddHealthChecks()
    .AddContainerRuntimeHealthCheck(
        name: "container-runtime",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready", "runtime"]);
```

## Map health-check endpoints

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
| Yes | `Healthy` — *"Container runtime 'Docker' is available."* |
| No | `Unhealthy` — *"Container runtime 'Docker' is not reachable."* |
| Exception | `Unhealthy` — exception details attached |

The health check never throws; any exception becomes an `Unhealthy` result.

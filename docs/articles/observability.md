# Observability

```bash
dotnet add package Bielu.Microservices.Orchestrator.OpenTelemetry
```

## Wire up instrumentation

`AddOpenTelemetryInstrumentation()` wraps every manager interface with tracing and metrics decorators, and starts a hosted service that periodically collects gauge metrics. Call it **after** registering a provider:

```csharp
using Bielu.Microservices.Orchestrator.OpenTelemetry.Extensions;

builder.Services.AddMicroservicesOrchestrator(orchestrator =>
{
    orchestrator
        .AddDocker()
        .AddOpenTelemetryInstrumentation(); // must come after the provider
});
```

The OpenTelemetry decorators are always the outermost wrappers, so spans include the time spent in state tracking.

Then register the library's `ActivitySource` and `Meter` with the OpenTelemetry SDK so they reach your exporter:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddOrchestratorInstrumentation()   // orchestrator spans
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddOrchestratorMetrics()           // orchestrator metrics
        .AddOtlpExporter());
```

## Traces

Every container, image, network and volume operation emits a span with semantic attributes such as `container.id`, `container.image`, `network.driver` and `volume.name`.

## Metrics

| Instrument | Unit | Description |
|---|---|---|
| `orchestrator.managed_instances.total` | | Managed microservice instances |
| `orchestrator.managed_instances.healthy` | | Managed instances with all containers running |
| `orchestrator.managed_instances.by_state` | | Managed instances per state |
| `orchestrator.managed_instances.desired_replicas.total` | | Desired replicas across all managed instances |
| `orchestrator.containers.total` | | Containers across all managed instances |
| `orchestrator.host.cpu.usage` | `%` | Host-level (system-wide) CPU usage |
| `orchestrator.host.memory.usage` | `%` | Host-level (system-wide) memory usage |
| `orchestrator.host.memory.available_bytes` | `By` | Available host memory |
| `orchestrator.host.memory.total_bytes` | `By` | Total host physical memory |

Host metrics are system-wide, not process-level.

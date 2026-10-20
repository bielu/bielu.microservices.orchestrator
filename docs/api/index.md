# API Reference

Generated from the XML documentation of every published package.

| Namespace | Package |
|---|---|
| `Bielu.Microservices.Orchestrator.*` | Core abstractions, models, builder and instance stores |
| `Bielu.Microservices.Orchestrator.Docker` | Docker runtime provider |
| `Bielu.Microservices.Orchestrator.Podman` | Podman runtime provider |
| `Bielu.Microservices.Orchestrator.Containerd` | containerd runtime provider |
| `Bielu.Microservices.Orchestrator.Kubernetes` | Kubernetes runtime provider |
| `Bielu.Microservices.Orchestrator.OpenTelemetry` | Tracing and metrics |
| `Bielu.Microservices.Orchestrator.HealthChecks` | ASP.NET Core health checks |
| `Bielu.Microservices.Orchestrator.Storage.EfCore` | EF Core instance store |
| `Bielu.Microservices.Orchestrator.Storage.File` | File-based instance store |

Start with <xref:Bielu.Microservices.Orchestrator.Abstractions.IContainerOrchestrator>, the entry point that aggregates every manager.

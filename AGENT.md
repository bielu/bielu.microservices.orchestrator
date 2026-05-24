# AGENT.md — bielu.microservices.orchestrator

This file describes the architecture, conventions, and coding principles for AI agents and developers working on this repository.

---

## Project Overview

`bielu.microservices.orchestrator` is a .NET library that provides a **unified abstraction layer** for managing container runtimes — Docker, Podman, containerd, and Kubernetes. The goal is: write container-management logic once, swap the runtime with a single configuration change.

**Primary language:** C#  
**Runtime target:** .NET (modern, DI-first)  
**Key integrations:** OpenTelemetry, ASP.NET Core Health Checks, Microsoft.Extensions.DependencyInjection, .NET Aspire

---

## Repository Structure

```
/
├── .github/workflows/     # CI/CD pipelines
├── scripts/               # Build and utility scripts
├── src/
│   ├── Bielu.Microservices.Orchestrator/                  # Core abstractions, models, builder
│   ├── Bielu.Microservices.Orchestrator.Docker/           # Docker runtime provider
│   ├── Bielu.Microservices.Orchestrator.Podman/           # Podman runtime provider
│   ├── Bielu.Microservices.Orchestrator.Containerd/       # containerd gRPC provider
│   ├── Bielu.Microservices.Orchestrator.Kubernetes/       # Kubernetes provider
│   ├── Bielu.Microservices.Orchestrator.OpenTelemetry/    # Tracing decorators
│   ├── Bielu.Microservices.Orchestrator.HealthChecks/     # ASP.NET Core health checks
│   └── Bielu.Microservices.Orchestrator.Examples.Api/     # Reference ASP.NET Core app
├── CODEOWNERS
├── LICENSE.md
├── readme.md
└── version.props
```

---

## Core Design Principles

### KISS — Keep It Simple, Stupid

- Prefer simple, readable code over clever abstractions. If a solution feels complex, step back and simplify.
- Each method should do one thing and do it clearly. Avoid deep nesting; extract helper methods when logic grows.
- Caller code should read naturally. For example: `await orchestrator.Containers.StartAsync(id)` — the intent is obvious with no preamble.
- Avoid premature optimization. Solve the problem at hand; optimize only when profiling identifies a bottleneck.
- Default options should work out of the box. Configuration should be opt-in, not required.

### DRY — Don't Repeat Yourself

- All runtime-agnostic logic belongs in the **core** project (`Bielu.Microservices.Orchestrator`). Provider projects must not duplicate this logic.
- Shared models (`CreateContainerRequest`, `PortMapping`, `PullImageRequest`, etc.) are defined once in core and reused by all providers.
- Extension methods for DI registration (`AddMicroservicesOrchestrator`, `AddDocker`, `AddOpenTelemetryInstrumentation`, etc.) follow a single consistent pattern — do not invent parallel registration mechanisms.
- OpenTelemetry spans are added via a **decorator pattern** on the manager interfaces — not reimplemented inside each provider.
- If you find yourself copying logic between providers, extract it into a shared base or utility in core.

### SOLID Principles

**Single Responsibility**  
Each class has one clear reason to change. Managers (`IContainerManager`, `IImageManager`, `INetworkManager`, `IVolumeManager`) are separate concerns. Do not combine unrelated responsibilities into one class or interface.

**Open/Closed**  
The orchestrator is open to extension (add a new provider) and closed for modification (existing interfaces and core logic do not change when adding a provider). New runtimes are added by implementing the existing interfaces, not by modifying them.

**Liskov Substitution**  
Any provider registered in DI must be a full and correct implementation of its interface. If a runtime does not support a feature (e.g., `ScaleAsync`), throw `NotSupportedException` — do not silently no-op or return incorrect data.

**Interface Segregation**  
Interfaces are split by concern: `IContainerManager`, `IImageManager`, `INetworkManager`, `IVolumeManager`. Do not merge these into a single monolithic interface. Callers should depend only on the interface they need.

**Dependency Inversion**  
All application code depends on the abstractions in core, never on concrete provider implementations. Providers are resolved via DI. Never `new` up a runtime client directly in business logic.

---

## Feature Slicing

This project is organised by **feature slice**, not by technical layer. Each slice owns everything it needs to deliver one coherent capability — its abstractions, its implementation, its DI registration, and its configuration — and is packaged as a standalone NuGet package. Consumers install only the slices they need.

### What a slice looks like

| Slice | Package | What it owns |
|---|---|---|
| Core abstractions | `Bielu.Microservices.Orchestrator` | Interfaces, models, builder |
| Docker runtime | `Bielu.Microservices.Orchestrator.Docker` | `DockerContainerManager`, `DockerImageManager`, etc. + `AddDocker()` |
| Kubernetes runtime | `Bielu.Microservices.Orchestrator.Kubernetes` | Kubernetes implementations + `AddKubernetes()` |
| Observability | `Bielu.Microservices.Orchestrator.OpenTelemetry` | Tracing decorators + `AddOpenTelemetryInstrumentation()` |
| Health checks | `Bielu.Microservices.Orchestrator.HealthChecks` | `IHealthCheck` impl + `AddContainerRuntimeHealthCheck()` |

### Rules for feature slices

- **A slice is the unit of change.** When adding a new runtime, you create a new project — you do not touch existing slices.
- **Slices depend inward, never sideways.** Provider slices (`Docker`, `Podman`, etc.) depend on core. They must never depend on each other.
- **Each slice registers itself.** DI wiring lives inside the slice via its own extension method. Nothing outside the slice should call `services.AddSingleton<DockerContainerManager>()` directly.
- **Cross-cutting concerns are their own slices.** OpenTelemetry and HealthChecks are separate packages, not bolted onto providers. A provider must work correctly without them installed.
- **Keep slice boundaries hard.** If you feel the urge to reference an internal type from another provider package, that type belongs in core instead.
- **New features ship as new slices or extend existing ones — not as modifications to unrelated slices.** For example, adding a metrics decorator follows the same pattern as the OpenTelemetry slice, not a change to Docker internals.

### Slice vs. layer

Do not organise code by technical layer at the top level of a slice (e.g., a root-level `Services/`, `Models/`, `Controllers/` folder structure). Instead, group files by the feature they deliver.

If a slice delivers a single feature, keep files flat in the project root alongside each other — `DockerContainerManager.cs`, `DockerOptions.cs`, `DockerExtensions.cs`.

If a slice delivers **multiple features**, create a `Features/` folder and give each feature its own subfolder with layer folders inside it:

```
Features/
├── ContainerLifecycle/
│   ├── Services/
│   └── Controllers/
├── ImageManagement/
│   ├── Services/
│   └── Controllers/
└── Networking/
    ├── Services/
    └── Controllers/
```

The rule is: **feature first, layer second** — never the other way around. A developer looking for networking logic should navigate to `Features/Networking/`, not hunt through a top-level `Services/` folder containing everything from every feature mixed together.

---

## Key Interfaces

| Interface | Responsibility |
|---|---|
| `IContainerOrchestrator` | Entry point — aggregates all managers and exposes `ProviderName` / `IsAvailableAsync()` |
| `IContainerManager` | Container lifecycle: list, get, create, start, stop, remove, logs, scale |
| `IImageManager` | Image management: list, get, pull, tag, remove |
| `INetworkManager` | Network management: list, create, remove, connect, disconnect |
| `IVolumeManager` | Volume management: list, create, remove |

---

## Coding Conventions

### General

- Use `async`/`await` throughout. Never block with `.Result` or `.Wait()`.
- All public methods that perform I/O must return `Task` or `Task<T>` and accept a `CancellationToken` where appropriate.
- Use C# primary constructors and records where they reduce boilerplate without sacrificing clarity.
- Prefer `using` declarations over `using` blocks unless scope control is needed.
- Null-check parameters at public API boundaries. Use `ArgumentNullException.ThrowIfNull`.

### Naming

- Interfaces: `I` prefix, noun or noun-phrase (e.g., `IContainerManager`).
- Implementations: descriptive suffix indicating the runtime (e.g., `DockerContainerManager`).
- Extension method classes: `[Feature]Extensions` (e.g., `DockerExtensions`, `HealthCheckExtensions`).
- Models: plain descriptive nouns (e.g., `CreateContainerRequest`, `PortMapping`).

### Dependency Injection

- All services are registered via extension methods on `IServiceCollection` or the orchestrator builder.
- Providers must not register themselves globally — they are registered only when explicitly called (e.g., `orchestrator.AddDocker()`).
- Use the builder pattern for orchestrator configuration. The fluent chain must be readable left-to-right.

### Error Handling

- Propagate exceptions from the underlying runtime client. Do not swallow exceptions silently.
- If a runtime does not support an operation, throw `NotSupportedException` with a descriptive message.
- Health check implementations must catch all exceptions and return an `Unhealthy` result with the exception attached — never throw out of a health check.

### OpenTelemetry

- Tracing is added via the decorator pattern — `AddOpenTelemetryInstrumentation()` wraps registered managers.
- Call `AddOpenTelemetryInstrumentation()` **after** the provider registration in the builder chain.
- Span attributes must use semantic naming: `container.id`, `container.image`, `network.driver`, `volume.name`.
- Do not embed tracing code directly inside provider implementations.

### Tests and Examples

- The `Examples.Api` project is the canonical reference for correct usage. Keep it up to date when adding features.
- New providers must include tests that validate the full container lifecycle (create → start → stop → remove).

---

## Adding a New Runtime Provider

1. Create a new project: `Bielu.Microservices.Orchestrator.<RuntimeName>`.
2. Implement `IContainerManager`, `IImageManager`, `INetworkManager`, `IVolumeManager`, and `IContainerOrchestrator`.
3. Add a DI extension method: `AddXxx(this IOrchestratorBuilder builder, Action<XxxOptions>? configure = null)`.
4. For unsupported operations, throw `NotSupportedException` — do not silently succeed.
5. Register the new provider in the builder and document any required configuration (endpoint, namespace, socket path, etc.).
6. Update `readme.md` with the install command and a registration example.

---

## What Agents Should Not Do

- Do not modify core interfaces without a clear cross-cutting reason — changes break all providers simultaneously.
- Do not add provider-specific types or concepts to the core project.
- Do not duplicate request/response models across provider projects.
- Do not register services at application startup without going through the orchestrator builder.
- Do not add hard-coded runtime assumptions into shared logic.
- Do not use `Thread.Sleep` or blocking calls in async paths.

---

## License

MIT — see `LICENSE.md`.
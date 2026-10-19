# AGENTS.md — bielu.microservices.orchestrator

This file describes the architecture, conventions, and coding principles for AI agents and developers working on this repository.

---

## Project Overview

`bielu.microservices.orchestrator` is a .NET library that provides a **unified abstraction layer** for managing container runtimes — Docker, Podman, containerd, and Kubernetes. The goal is: write container-management logic once, swap the runtime with a single configuration change.

**Primary language:** C#  
**Runtime target:** .NET (modern, DI-first)  
**Key integrations:** OpenTelemetry, ASP.NET Core Health Checks, Microsoft.Extensions.DependencyInjection, .NET Aspire

---

## Repository Structure

The solution is `src/Bielu.Microservices.Orchestrator.slnx`.

```
/
├── .changeset/            # Pending changesets (see "Versioning and changesets")
├── .github/workflows/     # CI/CD pipelines
├── scripts/               # Build and utility scripts (proto update, release helpers)
├── src/
│   ├── Bielu.Microservices.Orchestrator/                  # Core abstractions, models, builder
│   ├── Bielu.Microservices.Orchestrator.Docker/           # Docker runtime provider
│   ├── Bielu.Microservices.Orchestrator.Podman/           # Podman runtime provider
│   ├── Bielu.Microservices.Orchestrator.Containerd/       # containerd gRPC provider (vendored protos)
│   ├── Bielu.Microservices.Orchestrator.Kubernetes/       # Kubernetes provider
│   ├── Bielu.Microservices.Orchestrator.OpenTelemetry/    # Tracing decorators
│   ├── Bielu.Microservices.Orchestrator.HealthChecks/     # ASP.NET Core health checks
│   ├── Bielu.Microservices.Orchestrator.Storage.EfCore/   # EF Core instance store
│   ├── Bielu.Microservices.Orchestrator.Storage.File/     # File-based instance store
│   ├── Bielu.Microservices.Orchestrator.Tests/            # xUnit test suite
│   ├── Bielu.Microservices.Orchestrator.Examples.Api/     # Reference ASP.NET Core app
│   ├── Bielu.Microservices.Orchestrator.Examples.Worker/  # Reference worker
│   ├── Bielu.Microservices.Orchestrator.Examples.AppHost/ # .NET Aspire AppHost for the examples
│   ├── Bielu.Microservices.Orchestrator.Examples.ServiceDefaults/
│   ├── Directory.Build.props                              # Shared MSBuild/package metadata
│   └── Directory.Packages.props                           # Central package versions
├── .containerd-proto-version                              # containerd tag the protos come from
├── CHANGELOG.md
├── CODEOWNERS
├── LICENSE.md
├── package.json           # changesets tooling only, never published
├── readme.md
└── version.props          # Single shared version for every package
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

### Project settings

- **Target framework:** `net10.0`, `LangVersion` 14. Requires the **.NET 10 SDK**.
- `Nullable` and `ImplicitUsings` are enabled per project. Respect nullable annotations — don't suppress with `!` except in test arrange/assert.
- **Central package management:** all package versions live in `src/Directory.Packages.props`. Reference packages with `<PackageReference Include="..." />` and **no `Version` attribute**; add new versions to that file.
- **Package metadata** (license, copyright, XML documentation file, Source Link, `.snupkg` symbols, deterministic CI builds) is set once in `src/Directory.Build.props` — don't repeat it in csproj files. A package gets its own nuget.org landing page by adding a `PACKAGE.md` next to its csproj; otherwise the root `readme.md` is packed.
- **Shared MSBuild properties** live in `src/Directory.Build.props`. The **version** is centralized in `version.props` — never hardcode versions in csproj files, and never bump it by hand (see "Versioning and changesets").
- **Public API tracking:** projects with a `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` next to their csproj (currently the core `Bielu.Microservices.Orchestrator`) are checked by `Microsoft.CodeAnalysis.PublicApiAnalyzers`; `src/Directory.Build.props` turns it on for any project with those files. When you change the public surface, update `PublicAPI.Unshipped.txt`, or the build fails (RS0016/RS0017). Never edit `PublicAPI.Shipped.txt` by hand: `scripts/ship-public-api.mjs` moves Unshipped into Shipped as part of `npm run version` when a stable version is released. To opt another project in, add both files (`#nullable enable` header) and run `dotnet format analyzers <csproj> --diagnostics RS0016 --severity info`.
- **`InternalsVisibleTo`:** grant internals to `Bielu.Microservices.Orchestrator.Tests` (as the OpenTelemetry project does) and prefer `internal` over `public` for implementation details. Don't widen the public API surface without need.

### General

- Use `async`/`await` throughout. Never block with `.Result` or `.Wait()`.
- All public methods that perform I/O must return `Task` or `Task<T>` and accept a `CancellationToken` where appropriate.
- Use C# primary constructors and records where they reduce boilerplate without sacrificing clarity.
- Prefer `using` declarations over `using` blocks unless scope control is needed.
- Null-check parameters at public API boundaries. Use `ArgumentNullException.ThrowIfNull`.
- Use file-scoped namespaces.
- Public APIs get XML doc comments.
- Configuration uses fluent, chainable extension methods / options delegates returning the same object.
- Match the style of the file you're editing rather than imposing a single global style.
- **Formatting** is governed by `.editorconfig` (4-space indent for C#, UTF-8 BOM, final newline). Run `dotnet format whitespace src/Bielu.Microservices.Orchestrator.slnx` before committing; CI's `format-check` job runs it with `--verify-no-changes` on every PR.

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
- **xUnit** + **Shouldly** assertions + **NSubstitute** for mocking; Aspire integration tests use `Aspire.Hosting.Testing`.
- Follow the **Arrange / Act / Assert** comment structure used in the existing tests.
- Name tests `Method_Scenario_ExpectedResult` (e.g. `SaveAsync_OverwritesExistingInstance`).
- Add or update tests for any behavior change, covering the happy path and argument-validation/error paths. Never delete or weaken tests to make CI pass.

### Code coverage

Target **70%+ line coverage** for code you add or change. Coverage is collected with `coverlet.collector`, and a plain `dotnet test` collects it: `coverlet.runsettings`, applied to every test project via the root `Directory.Build.props`, enables the collector and writes Cobertura reports under each test project's `TestResults/`. Only `Bielu.*` library assemblies are measured; generated sources (`obj/`, including the containerd gRPC stubs), examples and test assemblies are excluded. Every test project must reference `coverlet.collector`, or its packages silently drop out of the report.

```bash
dotnet test src/Bielu.Microservices.Orchestrator.Tests
```

---

## Build and test

```bash
# Restore + build the whole solution
dotnet build src/Bielu.Microservices.Orchestrator.slnx

# Run all tests
dotnet test src/Bielu.Microservices.Orchestrator.Tests
```

---

## Versioning and changesets

Every NuGet package in this repository shares one version (`version.props`), driven by [changesets](https://github.com/changesets/changesets). See `.changeset/README.md`.

- **Every PR must add a changeset** — CI's `changeset-check` job fails without one. Run `npx changeset` (package `bielu-microservices-orchestrator`, bump `patch`/`minor`/`major` per semver), or `npx changeset add --empty` for chore-only PRs (CI, formatting, tests).
- Do not edit `version.props` or `CHANGELOG.md` by hand for a release: merging to `main` opens a "Version Packages" PR that runs `npm run version` (`changeset version` + `scripts/apply-nuget-version.mjs`).

---

## containerd protos

The containerd provider compiles vendored gRPC `.proto` files under `src/Bielu.Microservices.Orchestrator.Containerd/Proto/` with `Grpc.Tools`; the C# stubs are generated into `obj/` at build time.

- **Never hand-edit the vendored `.proto` files or the generated C#.** Update them only with `./scripts/update-containerd-protos.sh <tag>`, which downloads from the containerd tag, applies the C# patches (import-path rewrites, `go_package` → `csharp_namespace`, `Descriptor` → `ContentDescriptor` rename) and records the tag in `.containerd-proto-version`. On Windows/Git Bash run it with `LC_ALL=C.UTF-8` (it uses `grep -P`).
- If a new upstream version adds an import or a proto package, extend `PROTO_MAP` / `NS_MAP` in the script and the `<Protobuf>` items in the csproj — don't patch files manually.
- The `Check containerd Proto Updates` workflow opens these PRs automatically. Its PRs are created with `GITHUB_TOKEN`, so CI does not run on them — build and test locally before merging.
- Upstream `api/` follows containerd's [Public API Stability](https://github.com/containerd/containerd/blob/main/RELEASES.md#public-api-stability) rules; check generated-type changes against our usage in the Containerd provider when bumping across minor versions.

---

## Before opening a PR

1. `dotnet build src/Bielu.Microservices.Orchestrator.slnx` succeeds with no errors.
2. `dotnet test src/Bielu.Microservices.Orchestrator.Tests` passes.
3. `dotnet format whitespace --verify-no-changes src/Bielu.Microservices.Orchestrator.slnx` is clean.
4. Public-API changes are reflected in `PublicAPI.Unshipped.txt`.
5. A changeset is included.
6. Public API changes have XML docs; `readme.md` is updated when behavior or public APIs change.
7. Target the `main` branch.

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
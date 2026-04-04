# Bielu.Microservices.Orchestrator

A .NET library for managing container runtimes (Docker, Podman, containerd, and Kubernetes) through a unified abstraction layer.

## Features

- **Unified API** - Single abstraction to manage containers across multiple runtimes
- **Docker Support** - Full Docker Engine API integration via Docker.DotNet
- **Podman Support** - Podman-compatible API integration (Docker API compatible)
- **Containerd Support** - gRPC-based containerd runtime integration
- **Kubernetes Support** - Kubernetes API integration for pod/deployment management
- **Dependency Injection** - First-class Microsoft.Extensions.DependencyInjection support
- **Aspire Integration** - Example and test projects using .NET Aspire

## Getting Started

### Installation

```bash
dotnet add package Bielu.Microservices.Orchestrator
dotnet add package Bielu.Microservices.Orchestrator.Docker
```

### Basic Usage

```csharp
services.AddMicroservicesOrchestrator(builder =>
{
    builder.AddDocker(options =>
    {
        options.Endpoint = "unix:///var/run/docker.sock";
    });
});
```

## Packages

| Package | Description |
|---------|-------------|
| `Bielu.Microservices.Orchestrator` | Core abstractions and interfaces |
| `Bielu.Microservices.Orchestrator.Docker` | Docker runtime provider |
| `Bielu.Microservices.Orchestrator.Podman` | Podman runtime provider |
| `Bielu.Microservices.Orchestrator.Containerd` | containerd runtime provider |
| `Bielu.Microservices.Orchestrator.Kubernetes` | Kubernetes runtime provider |

## License

MIT

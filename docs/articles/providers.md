# Runtime Providers

Every provider implements the same manager interfaces, so switching runtimes is a one-line change. The rest of your code stays exactly the same:

```csharp
builder.Services.AddMicroservicesOrchestrator(orchestrator =>
{
    // Pick one:
    orchestrator.AddDocker();
    // orchestrator.AddPodman(o => o.Endpoint = "unix:///run/podman/podman.sock");
    // orchestrator.AddKubernetes(o => o.Namespace = "my-namespace");
    // orchestrator.AddContainerd(o => o.Endpoint = "unix:///run/containerd/containerd.sock");

    orchestrator.AddOpenTelemetryInstrumentation(); // optional, works with any provider
});
```

If a runtime cannot support an operation (for example `ScaleAsync` on a plain container engine), the provider throws `NotSupportedException` rather than silently doing nothing.

## Docker

`Bielu.Microservices.Orchestrator.Docker` — full Docker Engine API integration via Docker.DotNet.

| Option | Default | Description |
|---|---|---|
| `Endpoint` | `unix:///var/run/docker.sock` (Linux/macOS), `npipe://./pipe/docker_engine` (Windows) | Docker Engine API endpoint |
| `Timeout` | 30 s | Connection timeout |

## Podman

`Bielu.Microservices.Orchestrator.Podman` — uses the Docker-compatible Podman API; no extra code required.

| Option | Default | Description |
|---|---|---|
| `Endpoint` | platform-appropriate Podman socket | Podman API endpoint |
| `Timeout` | 30 s | Connection timeout |

## containerd

`Bielu.Microservices.Orchestrator.Containerd` — talks to containerd over gRPC. Networking is provided through CNI.

| Option | Default | Description |
|---|---|---|
| `Endpoint` | `unix:///run/containerd/containerd.sock` | containerd gRPC socket |
| `Namespace` | `default` | containerd namespace for all operations |
| `Timeout` | 30 s | Connection timeout |
| `CniConfigPath` | `/etc/cni/net.d` | Directory with CNI network configuration (`*.conf` / `*.conflist`) |
| `CniBinPath` | `/opt/cni/bin` | Directory with CNI plugin binaries |
| `CniDefaultSubnet` | `10.88.0.0/16` | Subnet for bridge networks created via `Networks.CreateAsync` |

The provider compiles the containerd gRPC `.proto` definitions, vendored from the containerd release recorded in [`.containerd-proto-version`](https://github.com/bielu/bielu.microservices.orchestrator/blob/main/.containerd-proto-version). A scheduled workflow opens a pull request whenever a new stable containerd release is published.

## Kubernetes

`Bielu.Microservices.Orchestrator.Kubernetes` — pod/deployment management via the official Kubernetes client.

| Option | Default | Description |
|---|---|---|
| `Namespace` | `default` | Kubernetes namespace for operations |
| `KubeConfigPath` | `null` | Path to a kubeconfig file; `null` uses in-cluster or the default config |
| `ApiServerUrl` | `null` | API server URL; `null` uses the kubeconfig |
| `UseInClusterConfig` | `false` | Use the in-cluster service account configuration |

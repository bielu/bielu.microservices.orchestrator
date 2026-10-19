# Images, Networks & Volumes

## Images

<xref:Bielu.Microservices.Orchestrator.Abstractions.IImageManager> (`orchestrator.Images`):

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

| Method | Description |
|--------|-------------|
| `ListAsync()` | List images |
| `GetAsync(string id)` | Inspect a single image |
| `PullAsync(PullImageRequest)` | Pull an image from a registry |
| `RemoveAsync(string id, bool force)` | Remove an image |
| `TagAsync(string id, string repo, string tag)` | Tag an image |

To roll managed instances onto a newer image, see [Image Updates](image-updates.md).

## Networks

<xref:Bielu.Microservices.Orchestrator.Abstractions.INetworkManager> (`orchestrator.Networks`):

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

`ConnectAsync` also accepts a <xref:Bielu.Microservices.Orchestrator.Models.NetworkAttachment>. Every provider honors its aliases; the Docker provider additionally maps static IPv4/IPv6 addresses, MAC address, gateway, links, driver options and DNS names.

On containerd, networks are CNI configurations; see the [containerd provider options](providers.md#containerd).

## Volumes

<xref:Bielu.Microservices.Orchestrator.Abstractions.IVolumeManager> (`orchestrator.Volumes`):

```csharp
// Create a volume
var volume = await orchestrator.Volumes.CreateAsync("my-volume", driver: "local");

// A local-driver volume bound to a host directory
var data = await orchestrator.Volumes.CreateLocalBoundAsync("app-data", "/srv/app-data");

// List and remove
var volumes = await orchestrator.Volumes.ListAsync();
await orchestrator.Volumes.RemoveAsync("my-volume", force: true);
```

`CreateLocalBoundAsync` is equivalent to `docker volume create --driver local --opt type=none --opt o=bind --opt device=<hostPath> <name>`; pass `LocalMountOptions` flags (e.g. `LocalMountOptions.Bind | LocalMountOptions.ReadOnly`) to change the mount.

# Image Updates

<xref:Bielu.Microservices.Orchestrator.Abstractions.IImageUpdateService> detects whether a newer version of a managed instance's image is available and recreates the instance's containers to adopt it. It is registered automatically by `AddMicroservicesOrchestrator`, and works on instances recorded by [state tracking](state-tracking.md), because it recreates containers from the persisted original request.

```csharp
public class Updater(IImageUpdateService updates)
{
    public async Task UpdateAllAsync(CancellationToken ct)
    {
        foreach (var status in await updates.CheckAllAsync(cancellationToken: ct))
        {
            if (status.UpdateAvailable)
            {
                var result = await updates.UpdateAsync(status.InstanceId, cancellationToken: ct);
                // result.Updated, result.PreviousDigest, ...
            }
        }
    }
}
```

| Method | Description |
|---|---|
| `CheckAsync(instanceId, options)` | Compares the current image digest with the registry |
| `CheckAllAsync(options)` | Checks every managed instance |
| `UpdateAsync(instanceId, options)` | Pulls the latest image and, if the digest changed, removes and recreates the instance's containers |

<xref:Bielu.Microservices.Orchestrator.Models.ImageUpdateOptions>:

| Option | Default | Description |
|---|---|---|
| `Pull` | `true` | Pull the image before comparing digests |
| `Force` | `false` | Recreate containers even when the digest is unchanged |

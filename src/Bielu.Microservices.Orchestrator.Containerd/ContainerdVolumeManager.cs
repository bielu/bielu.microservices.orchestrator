using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Containerd.Services.Snapshots.V1;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the volume manager.
/// containerd uses snapshotter-based storage rather than named volumes.
/// </summary>
public class ContainerdVolumeManager(
    Snapshots.SnapshotsClient snapshotsClient,
    ContainerdOptions options,
    ILogger<ContainerdVolumeManager> logger) : IVolumeManager
{
    public async Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing containerd snapshots in namespace {Namespace}", LogSanitizer.Sanitize(options.Namespace));

        var results = new List<VolumeInfo>();

        var call = snapshotsClient.List(new ListSnapshotsRequest(), NamespaceHeader(), cancellationToken: cancellationToken);
        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            results.AddRange(response.Info.Select(MapSnapshot));
        }

        return results.AsReadOnly();
    }

    public Task<VolumeInfo> CreateAsync(string name, string? driver = null, CancellationToken cancellationToken = default)
    {
        // containerd snapshots are created as part of the container/task lifecycle
        // (via Prepare/View), not as standalone named volumes.
        throw new NotSupportedException(
            "containerd does not support standalone named volumes. " +
            "Snapshots are managed automatically as part of container creation.");
    }

    public async Task RemoveAsync(string name, bool force = false, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Removing containerd snapshot {Name}", LogSanitizer.Sanitize(name));

        try
        {
            await snapshotsClient.RemoveAsync(
                new RemoveSnapshotRequest { Key = name }, NamespaceHeader(), cancellationToken: cancellationToken);

            logger.LogInformation("Removed containerd snapshot {Name}", LogSanitizer.Sanitize(name));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            if (!force)
            {
                throw;
            }
            logger.LogDebug("Snapshot {Name} not found during removal (force=true, ignoring)", LogSanitizer.Sanitize(name));
        }
    }

    private static VolumeInfo MapSnapshot(Info snapshot) =>
        new()
        {
            Name = snapshot.Name,
            Driver = "containerd-snapshotter",
            Labels = new Dictionary<string, string>(snapshot.Labels),
            CreatedAt = snapshot.CreatedAt != null
                ? DateTimeOffset.FromUnixTimeSeconds(snapshot.CreatedAt.Seconds)
                : DateTimeOffset.MinValue
        };

    private Metadata NamespaceHeader() =>
        new() { { "containerd-namespace", options.Namespace } };
}

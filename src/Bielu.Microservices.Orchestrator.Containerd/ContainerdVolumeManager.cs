using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the volume manager.
/// containerd uses snapshotter-based storage rather than named volumes.
/// </summary>
public class ContainerdVolumeManager(
    ContainerdOptions options,
    ILogger<ContainerdVolumeManager> logger) : IVolumeManager
{
    public Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing containerd snapshots in namespace {Namespace}", options.Namespace);
        return Task.FromResult<IReadOnlyList<VolumeInfo>>(new List<VolumeInfo>().AsReadOnly());
    }

    public Task<VolumeInfo> CreateAsync(string name, string? driver = null, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating containerd snapshot {Name}", name);
        throw new NotImplementedException("containerd uses snapshotters for storage management.");
    }

    public Task RemoveAsync(string name, bool force = false, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Removing containerd snapshot {Name}", name);
        throw new NotImplementedException("containerd uses snapshotters for storage management.");
    }
}

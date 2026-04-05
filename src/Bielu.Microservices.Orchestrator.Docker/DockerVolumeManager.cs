using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Docker;

/// <summary>
/// Docker implementation of the volume manager.
/// </summary>
public class DockerVolumeManager(
    DockerClient client,
    ILogger<DockerVolumeManager> logger) : IVolumeManager
{

    public async Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.Volumes.ListAsync(cancellationToken: cancellationToken);

        return response.Volumes?.Select(v => new VolumeInfo
        {
            Name = v.Name,
            Driver = v.Driver,
            MountPoint = v.Mountpoint,
            Labels = v.Labels != null ? new Dictionary<string, string>(v.Labels) : new Dictionary<string, string>(),
            CreatedAt = DateTimeOffset.TryParse(v.CreatedAt, out var created) ? created : DateTimeOffset.MinValue
        }).ToList().AsReadOnly() ?? new List<VolumeInfo>().AsReadOnly();
    }

    public async Task<VolumeInfo> CreateAsync(string name, string? driver = null, CancellationToken cancellationToken = default)
    {
        var response = await client.Volumes.CreateAsync(new VolumesCreateParameters
        {
            Name = name,
            Driver = driver ?? "local"
        }, cancellationToken);

        logger.LogInformation("Created volume {Name}", name);

        return new VolumeInfo
        {
            Name = response.Name,
            Driver = response.Driver,
            MountPoint = response.Mountpoint,
            Labels = response.Labels != null ? new Dictionary<string, string>(response.Labels) : new Dictionary<string, string>()
        };
    }

    public async Task RemoveAsync(string name, bool force = false, CancellationToken cancellationToken = default)
    {
        await client.Volumes.RemoveAsync(name, force, cancellationToken);
        logger.LogInformation("Removed volume {Name}", name);
    }
}

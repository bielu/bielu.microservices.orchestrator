using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using VolumeInfo = Bielu.Microservices.Orchestrator.Models.VolumeInfo;

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

        return response.Volumes?.Select(MapVolume).ToList().AsReadOnly()
               ?? new List<VolumeInfo>().AsReadOnly();
    }

    public async Task<VolumeInfo?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var volume = await client.Volumes.InspectAsync(name, cancellationToken);
            return MapVolume(volume);
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<VolumeInfo> CreateAsync(string name, string? driver = null, IDictionary<string, string>? driverOptions = null, CancellationToken cancellationToken = default)
    {
        var response = await client.Volumes.CreateAsync(new VolumesCreateParameters
        {
            Name = name,
            Driver = driver ?? "local",
            DriverOpts = driverOptions != null ? new Dictionary<string, string>(driverOptions) : null
        }, cancellationToken);

        logger.LogInformation("Created volume {Name}", name);

        return MapVolume(response);
    }

    public async Task RemoveAsync(string name, bool force = false, CancellationToken cancellationToken = default)
    {
        await client.Volumes.RemoveAsync(name, force, cancellationToken);
        logger.LogInformation("Removed volume {Name}", name);
    }

    private static VolumeInfo MapVolume(VolumeResponse v) => new()
    {
        Name = v.Name,
        Driver = v.Driver,
        MountPoint = v.Mountpoint,
        Labels = v.Labels != null ? new Dictionary<string, string>(v.Labels) : new Dictionary<string, string>(),
        DriverOptions = v.Options != null ? new Dictionary<string, string>(v.Options) : new Dictionary<string, string>(),
        CreatedAt = DateTimeOffset.TryParse(v.CreatedAt, out var created) ? created : DateTimeOffset.MinValue
    };
}

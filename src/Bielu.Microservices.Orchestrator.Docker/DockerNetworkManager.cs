using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Docker;

/// <summary>
/// Docker implementation of the network manager.
/// </summary>
public class DockerNetworkManager(
    DockerClient client,
    ILogger<DockerNetworkManager> logger) : INetworkManager
{

    public async Task<IReadOnlyList<NetworkInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var networks = await client.Networks.ListNetworksAsync(cancellationToken: cancellationToken);

        return networks.Select(n => new NetworkInfo
        {
            Id = n.ID,
            Name = n.Name,
            Driver = n.Driver,
            Labels = n.Labels != null ? new Dictionary<string, string>(n.Labels) : new Dictionary<string, string>()
        }).ToList().AsReadOnly();
    }

    public async Task<string> CreateAsync(string name, string driver = "bridge", CancellationToken cancellationToken = default)
    {
        var response = await client.Networks.CreateNetworkAsync(
            new NetworksCreateParameters { Name = name, Driver = driver }, cancellationToken);
        logger.LogInformation("Created network {NetworkId} with name {Name}", response.ID, name);
        return response.ID;
    }

    public async Task RemoveAsync(string networkId, CancellationToken cancellationToken = default)
    {
        await client.Networks.DeleteNetworkAsync(networkId, cancellationToken);
        logger.LogInformation("Removed network {NetworkId}", networkId);
    }

    public async Task ConnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        await client.Networks.ConnectNetworkAsync(networkId,
            new NetworkConnectParameters { Container = containerId }, cancellationToken);
        logger.LogInformation("Connected container {ContainerId} to network {NetworkId}", containerId, networkId);
    }

    public async Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        await client.Networks.DisconnectNetworkAsync(networkId,
            new NetworkDisconnectParameters { Container = containerId }, cancellationToken);
        logger.LogInformation("Disconnected container {ContainerId} from network {NetworkId}", containerId, networkId);
    }
}

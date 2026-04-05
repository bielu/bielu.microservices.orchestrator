using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the network manager.
/// Note: containerd has limited built-in networking; CNI plugins are typically used.
/// </summary>
public class ContainerdNetworkManager(
    ContainerdOptions options,
    ILogger<ContainerdNetworkManager> logger) : INetworkManager
{
    public Task<IReadOnlyList<NetworkInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing containerd networks (CNI) in namespace {Namespace}", options.Namespace);
        return Task.FromResult<IReadOnlyList<NetworkInfo>>(new List<NetworkInfo>().AsReadOnly());
    }

    public Task<string> CreateAsync(string name, string driver = "bridge", CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating containerd network {Name} with CNI plugin", name);
        throw new NotImplementedException("containerd networking requires CNI plugin configuration.");
    }

    public Task RemoveAsync(string networkId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Removing containerd network {NetworkId}", networkId);
        throw new NotImplementedException("containerd networking requires CNI plugin configuration.");
    }

    public Task ConnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Connecting container {ContainerId} to containerd network {NetworkId}", containerId, networkId);
        throw new NotImplementedException("containerd networking requires CNI plugin configuration.");
    }

    public Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Disconnecting container {ContainerId} from containerd network {NetworkId}", containerId, networkId);
        throw new NotImplementedException("containerd networking requires CNI plugin configuration.");
    }
}

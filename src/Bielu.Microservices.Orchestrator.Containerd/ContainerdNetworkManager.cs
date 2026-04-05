using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the network manager.
/// containerd has no built-in network management; CNI plugins handle networking externally.
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
        throw new NotSupportedException(
            "containerd does not manage networks. Configure networking via CNI plugins on the host.");
    }

    public Task RemoveAsync(string networkId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "containerd does not manage networks. Configure networking via CNI plugins on the host.");
    }

    public Task ConnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "containerd does not manage networks. Configure networking via CNI plugins on the host.");
    }

    public Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "containerd does not manage networks. Configure networking via CNI plugins on the host.");
    }
}

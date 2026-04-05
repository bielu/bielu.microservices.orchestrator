using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Abstractions;

/// <summary>
/// Manages container network operations.
/// </summary>
public interface INetworkManager
{
    /// <summary>
    /// Lists all networks.
    /// </summary>
    Task<IReadOnlyList<NetworkInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new network.
    /// </summary>
    Task<string> CreateAsync(string name, string driver = "bridge", CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a network.
    /// </summary>
    Task RemoveAsync(string networkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects a container to a network.
    /// </summary>
    Task ConnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects a container from a network.
    /// </summary>
    Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default);
}

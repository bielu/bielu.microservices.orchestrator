using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Abstractions;

/// <summary>
/// Manages container lifecycle operations.
/// </summary>
public interface IContainerManager
{
    string HostAddress { get; }
    /// <summary>
    /// Lists all containers.
    /// </summary>
    Task<IReadOnlyList<ContainerInfo>> ListAsync(bool all = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed information about a specific container.
    /// </summary>
    Task<ContainerInfo?> GetAsync(string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new container.
    /// </summary>
    Task<string> CreateAsync(CreateContainerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a container.
    /// </summary>
    Task StartAsync(string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a container.
    /// </summary>
    Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a container.
    /// </summary>
    Task RemoveAsync(string containerId, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the logs for a container.
    /// </summary>
    Task<string> GetLogsAsync(string containerId, bool stdout = true, bool stderr = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scales the number of instances for a container or container group.
    /// Not all runtimes support scaling; providers that do not will throw <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="containerId">The container identifier or group name to scale.</param>
    /// <param name="replicas">The desired number of instances.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ScaleAsync(string containerId, int replicas, CancellationToken cancellationToken = default);
}

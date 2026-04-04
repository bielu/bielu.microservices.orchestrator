namespace Bielu.Microservices.Orchestrator.Abstractions;

/// <summary>
/// Top-level orchestrator providing access to all container management capabilities.
/// </summary>
public interface IContainerOrchestrator
{
    /// <summary>
    /// Gets the container manager for this runtime.
    /// </summary>
    IContainerManager Containers { get; }

    /// <summary>
    /// Gets the image manager for this runtime.
    /// </summary>
    IImageManager Images { get; }

    /// <summary>
    /// Gets the network manager for this runtime.
    /// </summary>
    INetworkManager Networks { get; }

    /// <summary>
    /// Gets the volume manager for this runtime.
    /// </summary>
    IVolumeManager Volumes { get; }

    /// <summary>
    /// Gets the name of the container runtime provider (e.g., "Docker", "Podman").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Checks if the runtime is available and reachable.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

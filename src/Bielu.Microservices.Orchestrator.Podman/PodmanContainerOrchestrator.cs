using Bielu.Microservices.Orchestrator.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Podman;

/// <summary>
/// Podman implementation of the container orchestrator.
/// Podman uses a Docker-compatible API, so it delegates to Docker managers internally.
/// </summary>
public class PodmanContainerOrchestrator(
    IContainerManager containerManager,
    IImageManager imageManager,
    INetworkManager networkManager,
    IVolumeManager volumeManager,
    ILogger<PodmanContainerOrchestrator> logger) : IContainerOrchestrator
{
    /// <inheritdoc />
    public IContainerManager Containers { get; } = containerManager;

    /// <inheritdoc />
    public IImageManager Images { get; } = imageManager;

    /// <inheritdoc />
    public INetworkManager Networks { get; } = networkManager;

    /// <inheritdoc />
    public IVolumeManager Volumes { get; } = volumeManager;

    /// <inheritdoc />
    public string ProviderName => "Podman";

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Containers.ListAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Podman runtime is not available");
            return false;
        }
    }
}

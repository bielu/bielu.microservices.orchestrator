using Bielu.Microservices.Orchestrator.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Podman;

/// <summary>
/// Podman implementation of the container orchestrator.
/// Podman uses a Docker-compatible API, so it delegates to Docker managers internally.
/// </summary>
public class PodmanContainerOrchestrator : IContainerOrchestrator
{
    private readonly ILogger<PodmanContainerOrchestrator> _logger;

    public PodmanContainerOrchestrator(
        IContainerManager containerManager,
        IImageManager imageManager,
        INetworkManager networkManager,
        IVolumeManager volumeManager,
        ILogger<PodmanContainerOrchestrator> logger)
    {
        Containers = containerManager;
        Images = imageManager;
        Networks = networkManager;
        Volumes = volumeManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public IContainerManager Containers { get; }

    /// <inheritdoc />
    public IImageManager Images { get; }

    /// <inheritdoc />
    public INetworkManager Networks { get; }

    /// <inheritdoc />
    public IVolumeManager Volumes { get; }

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
            _logger.LogWarning(ex, "Podman runtime is not available");
            return false;
        }
    }
}

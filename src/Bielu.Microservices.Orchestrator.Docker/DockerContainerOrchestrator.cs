using Bielu.Microservices.Orchestrator.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Docker;

/// <summary>
/// Docker implementation of the container orchestrator.
/// </summary>
public class DockerContainerOrchestrator : IContainerOrchestrator
{
    private readonly ILogger<DockerContainerOrchestrator> _logger;

    public DockerContainerOrchestrator(
        IContainerManager containerManager,
        IImageManager imageManager,
        INetworkManager networkManager,
        IVolumeManager volumeManager,
        ILogger<DockerContainerOrchestrator> logger)
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
    public string ProviderName => "Docker";

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
            _logger.LogWarning(ex, "Docker runtime is not available");
            return false;
        }
    }
}

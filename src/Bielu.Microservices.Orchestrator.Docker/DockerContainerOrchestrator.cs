using Bielu.Microservices.Orchestrator.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Docker;

/// <summary>
/// Docker implementation of the container orchestrator.
/// </summary>
public class DockerContainerOrchestrator(
    IContainerManager containerManager,
    IImageManager imageManager,
    INetworkManager networkManager,
    IVolumeManager volumeManager,
    ILogger<DockerContainerOrchestrator> logger) : IContainerOrchestrator
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
            logger.LogWarning(ex, "Docker runtime is not available");
            return false;
        }
    }
}

using Bielu.Microservices.Orchestrator.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the container orchestrator using gRPC.
/// </summary>
public class ContainerdContainerOrchestrator(
    IContainerManager containerManager,
    IImageManager imageManager,
    INetworkManager networkManager,
    IVolumeManager volumeManager,
    ILogger<ContainerdContainerOrchestrator> logger) : IContainerOrchestrator
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
    public string ProviderName => "Containerd";

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Attempt to list containers as a health check
            await Containers.ListAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "containerd runtime is not available");
            return false;
        }
    }
}

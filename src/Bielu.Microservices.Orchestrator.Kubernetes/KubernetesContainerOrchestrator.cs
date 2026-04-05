using Bielu.Microservices.Orchestrator.Abstractions;
using k8s;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Kubernetes;

/// <summary>
/// Kubernetes implementation of the container orchestrator.
/// </summary>
public class KubernetesContainerOrchestrator(
    IContainerManager containerManager,
    IImageManager imageManager,
    INetworkManager networkManager,
    IVolumeManager volumeManager,
    IKubernetes client,
    ILogger<KubernetesContainerOrchestrator> logger) : IContainerOrchestrator
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
    public string ProviderName => "Kubernetes";

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await client.CoreV1.GetAPIResourcesAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kubernetes cluster is not available");
            return false;
        }
    }
}

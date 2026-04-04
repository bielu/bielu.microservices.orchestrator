using Bielu.Microservices.Orchestrator.Abstractions;
using k8s;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Kubernetes;

/// <summary>
/// Kubernetes implementation of the container orchestrator.
/// </summary>
public class KubernetesContainerOrchestrator : IContainerOrchestrator
{
    private readonly IKubernetes _client;
    private readonly ILogger<KubernetesContainerOrchestrator> _logger;

    public KubernetesContainerOrchestrator(
        IContainerManager containerManager,
        IImageManager imageManager,
        INetworkManager networkManager,
        IVolumeManager volumeManager,
        IKubernetes client,
        ILogger<KubernetesContainerOrchestrator> logger)
    {
        Containers = containerManager;
        Images = imageManager;
        Networks = networkManager;
        Volumes = volumeManager;
        _client = client;
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
    public string ProviderName => "Kubernetes";

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.CoreV1.GetAPIResourcesAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kubernetes cluster is not available");
            return false;
        }
    }
}

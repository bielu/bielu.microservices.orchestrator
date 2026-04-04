using Bielu.Microservices.Orchestrator.Abstractions;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the container orchestrator using gRPC.
/// </summary>
public class ContainerdContainerOrchestrator : IContainerOrchestrator
{
    private readonly GrpcChannel _channel;
    private readonly ILogger<ContainerdContainerOrchestrator> _logger;

    public ContainerdContainerOrchestrator(
        IContainerManager containerManager,
        IImageManager imageManager,
        INetworkManager networkManager,
        IVolumeManager volumeManager,
        GrpcChannel channel,
        ILogger<ContainerdContainerOrchestrator> logger)
    {
        Containers = containerManager;
        Images = imageManager;
        Networks = networkManager;
        Volumes = volumeManager;
        _channel = channel;
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
            _logger.LogWarning(ex, "containerd runtime is not available");
            return false;
        }
    }
}

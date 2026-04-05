using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Containerd.Services.Containers.V1;
using Containerd.Services.Images.V1;
using Containerd.Services.Snapshots.V1;
using Containerd.Services.Tasks.V1;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd.Extensions;

/// <summary>
/// Extension methods for registering containerd runtime provider.
/// </summary>
public static class ContainerdBuilderExtensions
{
    /// <summary>
    /// Adds the containerd runtime provider to the orchestrator.
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <param name="configure">A delegate to configure containerd options.</param>
    /// <returns>The orchestrator builder for chaining.</returns>
    public static OrchestratorBuilder AddContainerd(
        this OrchestratorBuilder builder,
        Action<ContainerdOptions>? configure = null)
    {
        var options = new ContainerdOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(_ =>
            GrpcChannel.ForAddress(options.Endpoint, new GrpcChannelOptions
            {
                Credentials = Grpc.Core.ChannelCredentials.Insecure
            }));

        // Register generated gRPC clients
        builder.Services.AddSingleton(sp => new Containers.ContainersClient(sp.GetRequiredService<GrpcChannel>()));
        builder.Services.AddSingleton(sp => new Tasks.TasksClient(sp.GetRequiredService<GrpcChannel>()));
        builder.Services.AddSingleton(sp => new Images.ImagesClient(sp.GetRequiredService<GrpcChannel>()));
        builder.Services.AddSingleton(sp => new Snapshots.SnapshotsClient(sp.GetRequiredService<GrpcChannel>()));

        builder.Services.TryAddSingleton<IContainerManager, ContainerdContainerManager>();
        builder.Services.TryAddSingleton<IImageManager, ContainerdImageManager>();
        builder.Services.TryAddSingleton<INetworkManager, ContainerdNetworkManager>();
        builder.Services.TryAddSingleton<IVolumeManager, ContainerdVolumeManager>();
        builder.Services.TryAddSingleton<IContainerOrchestrator>(sp =>
            new ContainerdContainerOrchestrator(
                sp.GetRequiredService<IContainerManager>(),
                sp.GetRequiredService<IImageManager>(),
                sp.GetRequiredService<INetworkManager>(),
                sp.GetRequiredService<IVolumeManager>(),
                sp.GetRequiredService<ILogger<ContainerdContainerOrchestrator>>()));

        return builder;
    }
}

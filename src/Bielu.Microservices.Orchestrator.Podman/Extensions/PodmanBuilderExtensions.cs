using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Docker;
using Bielu.Microservices.Orchestrator.Podman.Configuration;
using Docker.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bielu.Microservices.Orchestrator.Podman.Extensions;

/// <summary>
/// Extension methods for registering Podman runtime provider.
/// Podman exposes a Docker-compatible API, so Docker managers are reused.
/// </summary>
public static class PodmanBuilderExtensions
{
    /// <summary>
    /// Adds the Podman runtime provider to the orchestrator.
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <param name="configure">A delegate to configure Podman options.</param>
    /// <returns>The orchestrator builder for chaining.</returns>
    public static OrchestratorBuilder AddPodman(
        this OrchestratorBuilder builder,
        Action<PodmanOptions>? configure = null)
    {
        var options = new PodmanOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<DockerClient>(_ =>
        {
            var config = new DockerClientConfiguration(new Uri(options.Endpoint));
            return config.CreateClient();
        });

        // Podman uses Docker-compatible API, reuse Docker managers
        builder.Services.TryAddSingleton<IContainerManager, DockerContainerManager>();
        builder.Services.TryAddSingleton<IImageManager, DockerImageManager>();
        builder.Services.TryAddSingleton<INetworkManager, DockerNetworkManager>();
        builder.Services.TryAddSingleton<IVolumeManager, DockerVolumeManager>();
        builder.Services.TryAddSingleton<IContainerOrchestrator, PodmanContainerOrchestrator>();

        return builder;
    }
}

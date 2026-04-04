using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Docker.Configuration;
using Docker.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bielu.Microservices.Orchestrator.Docker.Extensions;

/// <summary>
/// Extension methods for registering Docker runtime provider.
/// </summary>
public static class DockerBuilderExtensions
{
    /// <summary>
    /// Adds the Docker runtime provider to the orchestrator.
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <param name="configure">A delegate to configure Docker options.</param>
    /// <returns>The orchestrator builder for chaining.</returns>
    public static OrchestratorBuilder AddDocker(
        this OrchestratorBuilder builder,
        Action<DockerOptions>? configure = null)
    {
        var options = new DockerOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<DockerClient>(_ =>
        {
            var config = new DockerClientConfiguration(new Uri(options.Endpoint));
            return config.CreateClient();
        });

        builder.Services.TryAddSingleton<IContainerManager, DockerContainerManager>();
        builder.Services.TryAddSingleton<IImageManager, DockerImageManager>();
        builder.Services.TryAddSingleton<INetworkManager, DockerNetworkManager>();
        builder.Services.TryAddSingleton<IVolumeManager, DockerVolumeManager>();
        builder.Services.TryAddSingleton<IContainerOrchestrator, DockerContainerOrchestrator>();

        return builder;
    }
}

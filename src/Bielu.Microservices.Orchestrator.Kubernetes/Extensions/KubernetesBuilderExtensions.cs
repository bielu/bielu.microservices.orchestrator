using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Kubernetes.Configuration;
using k8s;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bielu.Microservices.Orchestrator.Kubernetes.Extensions;

/// <summary>
/// Extension methods for registering Kubernetes runtime provider.
/// </summary>
public static class KubernetesBuilderExtensions
{
    /// <summary>
    /// Adds the Kubernetes runtime provider to the orchestrator.
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <param name="configure">A delegate to configure Kubernetes options.</param>
    /// <returns>The orchestrator builder for chaining.</returns>
    public static OrchestratorBuilder AddKubernetes(
        this OrchestratorBuilder builder,
        Action<KubernetesOptions>? configure = null)
    {
        var options = new KubernetesOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IKubernetes>(_ =>
        {
            KubernetesClientConfiguration config;

            if (options.UseInClusterConfig)
            {
                config = KubernetesClientConfiguration.InClusterConfig();
            }
            else if (!string.IsNullOrEmpty(options.KubeConfigPath))
            {
                config = KubernetesClientConfiguration.BuildConfigFromConfigFile(options.KubeConfigPath);
            }
            else
            {
                config = KubernetesClientConfiguration.BuildDefaultConfig();
            }

            if (!string.IsNullOrEmpty(options.ApiServerUrl))
            {
                config.Host = options.ApiServerUrl;
            }

            return new k8s.Kubernetes(config);
        });

        builder.Services.TryAddSingleton<IContainerManager, KubernetesContainerManager>();
        builder.Services.TryAddSingleton<IImageManager, KubernetesImageManager>();
        builder.Services.TryAddSingleton<INetworkManager, KubernetesNetworkManager>();
        builder.Services.TryAddSingleton<IVolumeManager, KubernetesVolumeManager>();
        builder.Services.TryAddSingleton<IContainerOrchestrator, KubernetesContainerOrchestrator>();

        return builder;
    }
}

using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Kubernetes.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using k8s;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Kubernetes;

/// <summary>
/// Kubernetes implementation of the network manager.
/// Maps to Kubernetes Service and NetworkPolicy resources.
/// </summary>
public class KubernetesNetworkManager(
    IKubernetes client,
    KubernetesOptions options,
    ILogger<KubernetesNetworkManager> logger) : INetworkManager
{

    public async Task<IReadOnlyList<NetworkInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var services = await client.CoreV1.ListNamespacedServiceAsync(options.Namespace, cancellationToken: cancellationToken);

        return services.Items.Select(s => new NetworkInfo
        {
            Id = s.Metadata.Uid ?? string.Empty,
            Name = s.Metadata.Name ?? string.Empty,
            Driver = s.Spec.Type ?? "ClusterIP",
            Labels = s.Metadata.Labels != null
                ? new Dictionary<string, string>(s.Metadata.Labels)
                : new Dictionary<string, string>()
        }).ToList().AsReadOnly();
    }

    public async Task<string> CreateAsync(string name, string driver = "ClusterIP", CancellationToken cancellationToken = default)
    {
        var service = new k8s.Models.V1Service
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = options.Namespace
            },
            Spec = new k8s.Models.V1ServiceSpec
            {
                Type = driver
            }
        };

        var created = await client.CoreV1.CreateNamespacedServiceAsync(service, options.Namespace, cancellationToken: cancellationToken);
        logger.LogInformation("Created Kubernetes service {ServiceName}", name);
        return created.Metadata.Uid ?? string.Empty;
    }

    public async Task RemoveAsync(string networkId, CancellationToken cancellationToken = default)
    {
        await client.CoreV1.DeleteNamespacedServiceAsync(networkId, options.Namespace, cancellationToken: cancellationToken);
        logger.LogInformation("Removed Kubernetes service {ServiceName}", networkId);
    }

    public Task ConnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Kubernetes uses label selectors for service routing. Service: {ServiceName}, Pod: {PodName}", networkId, containerId);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Kubernetes uses label selectors for service routing. Service: {ServiceName}, Pod: {PodName}", networkId, containerId);
        return Task.CompletedTask;
    }
}

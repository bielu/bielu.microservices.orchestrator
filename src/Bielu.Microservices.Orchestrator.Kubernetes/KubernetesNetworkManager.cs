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
public class KubernetesNetworkManager : INetworkManager
{
    private readonly IKubernetes _client;
    private readonly KubernetesOptions _options;
    private readonly ILogger<KubernetesNetworkManager> _logger;

    public KubernetesNetworkManager(IKubernetes client, KubernetesOptions options, ILogger<KubernetesNetworkManager> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NetworkInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var services = await _client.CoreV1.ListNamespacedServiceAsync(_options.Namespace, cancellationToken: cancellationToken);

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
                NamespaceProperty = _options.Namespace
            },
            Spec = new k8s.Models.V1ServiceSpec
            {
                Type = driver
            }
        };

        var created = await _client.CoreV1.CreateNamespacedServiceAsync(service, _options.Namespace, cancellationToken: cancellationToken);
        _logger.LogInformation("Created Kubernetes service {ServiceName}", name);
        return created.Metadata.Uid ?? string.Empty;
    }

    public async Task RemoveAsync(string networkId, CancellationToken cancellationToken = default)
    {
        await _client.CoreV1.DeleteNamespacedServiceAsync(networkId, _options.Namespace, cancellationToken: cancellationToken);
        _logger.LogInformation("Removed Kubernetes service {ServiceName}", networkId);
    }

    public Task ConnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Kubernetes uses label selectors for service routing. Service: {ServiceName}, Pod: {PodName}", networkId, containerId);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Kubernetes uses label selectors for service routing. Service: {ServiceName}, Pod: {PodName}", networkId, containerId);
        return Task.CompletedTask;
    }
}

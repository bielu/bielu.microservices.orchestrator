using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Kubernetes.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using k8s;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Kubernetes;

/// <summary>
/// Kubernetes implementation of the volume manager.
/// Maps to Kubernetes PersistentVolumeClaim resources.
/// </summary>
public class KubernetesVolumeManager : IVolumeManager
{
    private readonly IKubernetes _client;
    private readonly KubernetesOptions _options;
    private readonly ILogger<KubernetesVolumeManager> _logger;

    public KubernetesVolumeManager(IKubernetes client, KubernetesOptions options, ILogger<KubernetesVolumeManager> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var pvcs = await _client.CoreV1.ListNamespacedPersistentVolumeClaimAsync(
            _options.Namespace, cancellationToken: cancellationToken);

        return pvcs.Items.Select(pvc => new VolumeInfo
        {
            Name = pvc.Metadata.Name ?? string.Empty,
            Driver = pvc.Spec.StorageClassName ?? string.Empty,
            MountPoint = pvc.Spec.VolumeName ?? string.Empty,
            Labels = pvc.Metadata.Labels != null
                ? new Dictionary<string, string>(pvc.Metadata.Labels)
                : new Dictionary<string, string>(),
            CreatedAt = pvc.Metadata.CreationTimestamp ?? DateTimeOffset.MinValue
        }).ToList().AsReadOnly();
    }

    public async Task<VolumeInfo> CreateAsync(string name, string? driver = null, CancellationToken cancellationToken = default)
    {
        var pvc = new k8s.Models.V1PersistentVolumeClaim
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = _options.Namespace
            },
            Spec = new k8s.Models.V1PersistentVolumeClaimSpec
            {
                AccessModes = new List<string> { "ReadWriteOnce" },
                StorageClassName = driver,
                Resources = new k8s.Models.V1VolumeResourceRequirements
                {
                    Requests = new Dictionary<string, k8s.Models.ResourceQuantity>
                    {
                        ["storage"] = new("1Gi")
                    }
                }
            }
        };

        var created = await _client.CoreV1.CreateNamespacedPersistentVolumeClaimAsync(
            pvc, _options.Namespace, cancellationToken: cancellationToken);

        _logger.LogInformation("Created Kubernetes PVC {PvcName}", name);

        return new VolumeInfo
        {
            Name = created.Metadata.Name ?? string.Empty,
            Driver = created.Spec.StorageClassName ?? string.Empty,
            Labels = created.Metadata.Labels != null
                ? new Dictionary<string, string>(created.Metadata.Labels)
                : new Dictionary<string, string>(),
            CreatedAt = created.Metadata.CreationTimestamp ?? DateTimeOffset.MinValue
        };
    }

    public async Task RemoveAsync(string name, bool force = false, CancellationToken cancellationToken = default)
    {
        await _client.CoreV1.DeleteNamespacedPersistentVolumeClaimAsync(
            name, _options.Namespace, cancellationToken: cancellationToken);
        _logger.LogInformation("Removed Kubernetes PVC {PvcName}", name);
    }
}

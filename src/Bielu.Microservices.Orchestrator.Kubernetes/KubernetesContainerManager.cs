using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Kubernetes.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using k8s;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Kubernetes;

/// <summary>
/// Kubernetes implementation of the container manager.
/// Maps container operations to Kubernetes Pod operations.
/// </summary>
public class KubernetesContainerManager(
    IKubernetes client,
    KubernetesOptions options,
    OrchestratorOptions orchestratorOptions,
    ILogger<KubernetesContainerManager> logger) : IContainerManager
{
    //todo: figure out how to get the host address
    public string HostAddress => "localhost";

    public async Task<IReadOnlyList<ContainerInfo>> ListAsync(bool all = false, CancellationToken cancellationToken = default)
    {
        var labelSelector = orchestratorOptions.ManagedContainersOnly
            ? $"{OrchestratorLabels.ManagedBy}={OrchestratorLabels.ManagedByValue}"
            : null;

        var pods = await client.CoreV1.ListNamespacedPodAsync(
            options.Namespace,
            labelSelector: labelSelector,
            cancellationToken: cancellationToken);

        return pods.Items.Select(pod => new ContainerInfo
        {
            Id = pod.Metadata.Uid ?? string.Empty,
            Name = pod.Metadata.Name ?? string.Empty,
            Image = pod.Spec.Containers.FirstOrDefault()?.Image ?? string.Empty,
            State = MapPodPhase(pod.Status?.Phase),
            CreatedAt = pod.Metadata.CreationTimestamp ?? DateTimeOffset.MinValue,
            Labels = pod.Metadata.Labels != null
                ? new Dictionary<string, string>(pod.Metadata.Labels)
                : new Dictionary<string, string>()
        }).ToList().AsReadOnly();
    }

    public async Task<ContainerInfo?> GetAsync(string containerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var pod = await client.CoreV1.ReadNamespacedPodAsync(containerId, options.Namespace, cancellationToken: cancellationToken);
            return new ContainerInfo
            {
                Id = pod.Metadata.Uid ?? string.Empty,
                Name = pod.Metadata.Name ?? string.Empty,
                Image = pod.Spec.Containers.FirstOrDefault()?.Image ?? string.Empty,
                State = MapPodPhase(pod.Status?.Phase),
                CreatedAt = pod.Metadata.CreationTimestamp ?? DateTimeOffset.MinValue,
                Labels = pod.Metadata.Labels != null
                    ? new Dictionary<string, string>(pod.Metadata.Labels)
                    : new Dictionary<string, string>()
            };
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> CreateAsync(CreateContainerRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Replicas <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Replicas must be at least 1.");
        }

        if (request.Replicas == 1)
        {
            return await CreateSinglePodAsync(request, request.Name, request.Labels, cancellationToken);
        }

        // Create multiple pods with indexed names and grouping labels
        var groupName = request.Name ?? $"orchestrator-{Guid.NewGuid():N}";
        string? firstName = null;

        for (var i = 0; i < request.Replicas; i++)
        {
            var replicaName = $"{groupName}-{i}";
            var replicaLabels = new Dictionary<string, string>(request.Labels)
            {
                [OrchestratorLabels.Group] = groupName,
                [OrchestratorLabels.ReplicaIndex] = i.ToString()
            };

            var name = await CreateSinglePodAsync(request, replicaName, replicaLabels, cancellationToken);
            firstName ??= name;
        }

        logger.LogInformation("Created {Replicas} Kubernetes pod replicas in group {GroupName}", request.Replicas, LogSanitizer.Sanitize(groupName));
        return firstName!;
    }

    /// <inheritdoc />
    public Task ScaleAsync(string containerId, int replicas, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Scaling bare Kubernetes pods is not supported. " +
            "Use Kubernetes Deployments, ReplicaSets, or StatefulSets for native scaling, " +
            "or create multiple pods with Replicas > 1 in CreateContainerRequest.");
    }

    public Task StartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        // Kubernetes pods start automatically upon creation
        logger.LogInformation("Kubernetes pod {PodName} starts automatically upon creation", LogSanitizer.Sanitize(containerId));
        return Task.CompletedTask;
    }

    public async Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        // In Kubernetes, stopping a pod means deleting it
        int? gracePeriod = timeout.HasValue ? (int)timeout.Value.TotalSeconds : null;
        await client.CoreV1.DeleteNamespacedPodAsync(
            containerId, options.Namespace,
            gracePeriodSeconds: gracePeriod,
            cancellationToken: cancellationToken);
        logger.LogInformation("Stopped (deleted) Kubernetes pod {PodName}", LogSanitizer.Sanitize(containerId));
    }

    public async Task RemoveAsync(string containerId, bool force = false, CancellationToken cancellationToken = default)
    {
        await client.CoreV1.DeleteNamespacedPodAsync(
            containerId, options.Namespace,
            gracePeriodSeconds: force ? 0 : null,
            cancellationToken: cancellationToken);
        logger.LogInformation("Removed Kubernetes pod {PodName}", LogSanitizer.Sanitize(containerId));
    }

    public async Task<string> GetLogsAsync(string containerId, bool stdout = true, bool stderr = true, CancellationToken cancellationToken = default)
    {
        var logStream = await client.CoreV1.ReadNamespacedPodLogAsync(
            containerId, options.Namespace, cancellationToken: cancellationToken);
        using var reader = new StreamReader(logStream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private async Task<string> CreateSinglePodAsync(
        CreateContainerRequest request,
        string? podName,
        IDictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        var podLabels = new Dictionary<string, string>(labels)
        {
            [OrchestratorLabels.ManagedBy] = OrchestratorLabels.ManagedByValue
        };

        var pod = new k8s.Models.V1Pod
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = podName ?? $"orchestrator-{Guid.NewGuid():N}",
                NamespaceProperty = options.Namespace,
                Labels = podLabels
            },
            Spec = new k8s.Models.V1PodSpec
            {
                Containers = new List<k8s.Models.V1Container>
                {
                    new()
                    {
                        Name = podName ?? "main",
                        Image = request.Image,
                        Command = request.Command?.ToList(),
                        Env = request.EnvironmentVariables.Select(kv =>
                            new k8s.Models.V1EnvVar { Name = kv.Key, Value = kv.Value }).ToList(),
                        Ports = request.Ports.Select(p =>
                            new k8s.Models.V1ContainerPort
                            {
                                ContainerPort = p.ContainerPort,
                                Protocol = p.Protocol.ToUpperInvariant()
                            }).ToList()
                    }
                },
                RestartPolicy = "Never"
            }
        };

        var created = await client.CoreV1.CreateNamespacedPodAsync(pod, options.Namespace, cancellationToken: cancellationToken);
        logger.LogInformation("Created Kubernetes pod {PodName} from image {Image}",
            LogSanitizer.Sanitize(created.Metadata.Name), LogSanitizer.Sanitize(request.Image));
        return created.Metadata.Name ?? string.Empty;
    }

    private static ContainerState MapPodPhase(string? phase)
    {
        return phase?.ToLowerInvariant() switch
        {
            "pending" => ContainerState.Created,
            "running" => ContainerState.Running,
            "succeeded" => ContainerState.Exited,
            "failed" => ContainerState.Dead,
            "unknown" => ContainerState.Unknown,
            _ => ContainerState.Unknown
        };
    }
}

using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Docker;

/// <summary>
/// Docker implementation of the container manager.
/// </summary>
public class DockerContainerManager(
    DockerClient client,
    OrchestratorOptions orchestratorOptions,
    ILogger<DockerContainerManager> logger) : IContainerManager
{
    public async Task<IReadOnlyList<ContainerInfo>> ListAsync(bool all = false, CancellationToken cancellationToken = default)
    {
        var listParams = new ContainersListParameters { All = all };

        if (orchestratorOptions.ManagedContainersOnly)
        {
            listParams.Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool>
                {
                    [$"{OrchestratorLabels.ManagedBy}={OrchestratorLabels.ManagedByValue}"] = true
                }
            };
        }

        var containers = await client.Containers.ListContainersAsync(listParams, cancellationToken);

        return containers.Select(c => new ContainerInfo
        {
            Id = c.ID,
            Name = c.Names?.FirstOrDefault()?.TrimStart('/') ?? string.Empty,
            Image = c.Image,
            State = MapState(c.State),
            CreatedAt = new DateTimeOffset(c.Created),
            Labels = c.Labels != null ? new Dictionary<string, string>(c.Labels) : new Dictionary<string, string>(),
            Ports = c.Ports?.Select(p => new PortMapping
            {
                ContainerPort = (int)p.PrivatePort,
                HostPort = (int)p.PublicPort,
                Protocol = p.Type,
                HostIp = p.IP ?? "0.0.0.0"
            }).ToList() ?? new List<PortMapping>()
        }).ToList().AsReadOnly();
    }

    public async Task<ContainerInfo?> GetAsync(string containerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
            return new ContainerInfo
            {
                Id = response.ID,
                Name = response.Name.TrimStart('/'),
                Image = response.Config.Image,
                State = MapState(response.State.Status),
                CreatedAt = new DateTimeOffset(response.Created),
                Labels = response.Config.Labels != null ? new Dictionary<string, string>(response.Config.Labels) : new Dictionary<string, string>(),
                EnvironmentVariables = ParseEnvironmentVariables(response.Config.Env)
            };
        }
        catch (DockerContainerNotFoundException)
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
            return await CreateSingleContainerAsync(request, request.Name, cancellationToken);
        }

        // Create multiple replicas with indexed names and a grouping label
        var groupName = request.Name ?? $"orchestrator-{Guid.NewGuid():N}";
        string? firstId = null;

        for (var i = 0; i < request.Replicas; i++)
        {
            var replicaName = $"{groupName}-{i}";
            var replicaLabels = new Dictionary<string, string>(request.Labels)
            {
                [OrchestratorLabels.Group] = groupName,
                [OrchestratorLabels.ReplicaIndex] = i.ToString()
            };

            var id = await CreateSingleContainerAsync(request, replicaName, cancellationToken, replicaLabels);
            firstId ??= id;
        }

        logger.LogInformation("Created {Replicas} container replicas in group {GroupName}", request.Replicas, LogSanitizer.Sanitize(groupName));
        return firstId!;
    }

    /// <inheritdoc />
    public Task ScaleAsync(string containerId, int replicas, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Docker does not natively support scaling a single container. " +
            "Use Docker Compose or Docker Swarm for scaling capabilities, " +
            "or create multiple containers with Replicas > 1 in CreateContainerRequest.");
    }

    public async Task StartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        await client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
        logger.LogInformation("Started container {ContainerId}", LogSanitizer.Sanitize(containerId));
    }

    public async Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var stopParams = new ContainerStopParameters();
        if (timeout.HasValue)
        {
            stopParams.WaitBeforeKillSeconds = (uint)timeout.Value.TotalSeconds;
        }

        await client.Containers.StopContainerAsync(containerId, stopParams, cancellationToken);
        logger.LogInformation("Stopped container {ContainerId}", LogSanitizer.Sanitize(containerId));
    }

    public async Task RemoveAsync(string containerId, bool force = false, CancellationToken cancellationToken = default)
    {
        await client.Containers.RemoveContainerAsync(containerId,
            new ContainerRemoveParameters { Force = force }, cancellationToken);
        logger.LogInformation("Removed container {ContainerId}", LogSanitizer.Sanitize(containerId));
    }

    public async Task<string> GetLogsAsync(string containerId, bool stdout = true, bool stderr = true, CancellationToken cancellationToken = default)
    {
        var logParams = new ContainerLogsParameters
        {
            ShowStdout = stdout,
            ShowStderr = stderr
        };

        using var logStream = await client.Containers.GetContainerLogsAsync(containerId, false, logParams, cancellationToken);
        using var memoryStream = new MemoryStream();
        await logStream.CopyOutputToAsync(Stream.Null, memoryStream, Stream.Null, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    private async Task<string> CreateSingleContainerAsync(
        CreateContainerRequest request,
        string? containerName,
        CancellationToken cancellationToken,
        Dictionary<string, string>? overrideLabels = null)
    {
        var labels = overrideLabels != null
            ? new Dictionary<string, string>(overrideLabels)
            : new Dictionary<string, string>(request.Labels);
        labels[OrchestratorLabels.ManagedBy] = OrchestratorLabels.ManagedByValue;

        var createParams = new CreateContainerParameters
        {
            Image = request.Image,
            Name = containerName,
            Env = request.EnvironmentVariables.Select(kv => $"{kv.Key}={kv.Value}").ToList(),
            Labels = labels,
            HostConfig = new HostConfig
            {
                PortBindings = request.Ports.ToDictionary(
                    p => $"{p.ContainerPort}/{p.Protocol}",
                    p => (IList<PortBinding>)new List<PortBinding>
                    {
                        new() { HostPort = p.HostPort.ToString(), HostIP = p.HostIp }
                    }),
                Binds = request.Volumes.ToList(),
                AutoRemove = request.AutoRemove
            }
        };

        if (request.Command is { Count: > 0 })
        {
            createParams.Cmd = request.Command.ToList();
        }

        var response = await client.Containers.CreateContainerAsync(createParams, cancellationToken);
        logger.LogInformation("Created container {ContainerId} from image {Image}",
            LogSanitizer.Sanitize(response.ID), LogSanitizer.Sanitize(request.Image));
        return response.ID;
    }

    private static Models.ContainerState MapState(string state)
    {
        return state?.ToLowerInvariant() switch
        {
            "created" => Models.ContainerState.Created,
            "running" => Models.ContainerState.Running,
            "paused" => Models.ContainerState.Paused,
            "restarting" => Models.ContainerState.Restarting,
            "removing" => Models.ContainerState.Removing,
            "exited" => Models.ContainerState.Exited,
            "dead" => Models.ContainerState.Dead,
            _ => Models.ContainerState.Unknown
        };
    }

    private static Dictionary<string, string> ParseEnvironmentVariables(IList<string>? envVars)
    {
        var result = new Dictionary<string, string>();
        if (envVars == null) return result;

        foreach (var env in envVars)
        {
            var separatorIndex = env.IndexOf('=');
            if (separatorIndex > 0)
            {
                result[env[..separatorIndex]] = env[(separatorIndex + 1)..];
            }
        }
        return result;
    }
}

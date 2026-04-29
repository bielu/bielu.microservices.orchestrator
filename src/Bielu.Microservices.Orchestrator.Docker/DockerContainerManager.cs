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
    IImageManager imageManager,
    INetworkManager networkManager,
    ILogger<DockerContainerManager> logger) : IContainerManager
{
    //todo: confirm default address
    public string HostAddress => "host.docker.internal";

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
                ContainerPort = p.PrivatePort,
                HostPort = p.PublicPort,
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
                EnvironmentVariables = ParseEnvironmentVariables(response.Config.Env),
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
        // Check if image exists locally, if not pull it
        await EnsureImageExistsAsync(request.Image, cancellationToken);

        var labels = overrideLabels != null
            ? new Dictionary<string, string>(overrideLabels)
            : new Dictionary<string, string>(request.Labels);
        labels[OrchestratorLabels.ManagedBy] = OrchestratorLabels.ManagedByValue;
        labels[OrchestratorLabels.ManagedById] = Guid.NewGuid().ToString();

        // Validate that all requested user-defined networks exist before attempting to create the container.
        // This produces a clearer error than the Docker daemon's generic 404 on container create.
        await EnsureNetworksExistAsync(request.Networks, cancellationToken);

        var primaryNetwork = request.Networks?.FirstOrDefault();
        var primaryIsSpecialMode = primaryNetwork != null && IsSpecialNetworkMode(primaryNetwork.NetworkName);
        var primaryIsAttachable = primaryNetwork != null && !primaryIsSpecialMode;

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
                AutoRemove = request.AutoRemove,
                // Use the first requested network as primary, or let the runtime choose the default.
                NetworkMode = primaryNetwork?.NetworkName
            },
            // Set aliases for the primary network during creation. Aliases are only valid on
            // user-defined networks, never on special modes (host/none/container:*) or the
            // default 'bridge' network.
            NetworkingConfig = primaryIsAttachable && CanUseAliases(primaryNetwork!.NetworkName, primaryNetwork.Aliases)
                ? new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [primaryNetwork!.NetworkName] = new EndpointSettings { Aliases = primaryNetwork.Aliases.ToList() }
                    }
                }
                : null
        };

        if (request.Command is { Count: > 0 })
        {
            createParams.Cmd = request.Command.ToList();
        }

        var response = await client.Containers.CreateContainerAsync(createParams, cancellationToken);

        // Connect any additional networks after creation. The primary network is already attached
        // at create time via NetworkMode/NetworkingConfig, so we skip it here to avoid a redundant
        // 409 Conflict round-trip. Special modes (host/none/container:*) cannot be connected to
        // additional networks, so we skip the post-create loop entirely in that case.
        if (request.Networks?.Count > 0 && !primaryIsSpecialMode)
        {
            foreach (var network in request.Networks.Skip(1))
            {
                if (IsSpecialNetworkMode(network.NetworkName))
                {
                    logger.LogWarning(
                        "Skipping connection of container {ContainerId} to network {Network}: special network modes (host/none/container:*) cannot be used as additional networks.",
                        LogSanitizer.Sanitize(response.ID), LogSanitizer.Sanitize(network.NetworkName));
                    continue;
                }

                var aliases = CanUseAliases(network.NetworkName, network.Aliases) ? network.Aliases : null;
                logger.LogInformation("Connecting container {ContainerId} to network {Network}",
                    LogSanitizer.Sanitize(response.ID), LogSanitizer.Sanitize(network.NetworkName));
                await networkManager.ConnectAsync(network.NetworkName, response.ID, aliases, cancellationToken);
            }
        }

        logger.LogInformation("Created container {ContainerId} from image {Image}",
            LogSanitizer.Sanitize(response.ID), LogSanitizer.Sanitize(request.Image));
        return response.ID;
    }

    private async Task EnsureNetworksExistAsync(IList<Models.NetworkAttachment>? networks, CancellationToken cancellationToken)
    {
        if (networks == null || networks.Count == 0)
        {
            return;
        }

        // Special modes (host/none/container:*) are not real networks and don't need to exist.
        var toCheck = networks
            .Select(n => n.NetworkName)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !IsSpecialNetworkMode(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (toCheck.Count == 0)
        {
            return;
        }

        var existing = await networkManager.ListAsync(cancellationToken);
        var existingNames = new HashSet<string>(existing.Select(n => n.Name), StringComparer.Ordinal);
        var existingIds = new HashSet<string>(existing.Select(n => n.Id), StringComparer.Ordinal);

        var missing = toCheck
            .Where(n => !existingNames.Contains(n) && !existingIds.Contains(n))
            .ToList();

        if (missing.Count > 0)
        {
            var joined = string.Join(", ", missing);
            throw new InvalidOperationException(
                $"The following Docker network(s) do not exist and must be created before attaching a container: {joined}.");
        }
    }

    private static bool IsSpecialNetworkMode(string networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName))
        {
            return false;
        }

        return networkName.Equals("host", StringComparison.OrdinalIgnoreCase)
               || networkName.Equals("none", StringComparison.OrdinalIgnoreCase)
               || networkName.Equals("default", StringComparison.OrdinalIgnoreCase)
               || networkName.StartsWith("container:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanUseAliases(string networkName, IList<string>? aliases)
    {
        if (aliases == null || aliases.Count == 0)
        {
            return false;
        }

        // Docker disallows network-scoped aliases on the default 'bridge' network.
        return !networkName.Equals("bridge", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureImageExistsAsync(string image, CancellationToken cancellationToken)
    {
        var images = await imageManager.ListAsync(cancellationToken);
        var imageExists = images.Any(img => img.Tags?.Contains(image) == true);

        if (!imageExists)
        {
            logger.LogInformation("Image {Image} not found locally, pulling from registry", LogSanitizer.Sanitize(image));
            await imageManager.PullAsync(new PullImageRequest { Image = image }, cancellationToken);
            logger.LogInformation("Successfully pulled image {Image}", LogSanitizer.Sanitize(image));
        }
    }

    private static Models.ContainerState MapState(string state)
    {
        return state.ToLowerInvariant() switch
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
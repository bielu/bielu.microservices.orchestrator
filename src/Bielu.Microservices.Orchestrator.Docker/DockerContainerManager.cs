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
    public string ProviderName => "Docker";

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
                HostPort = (int)(p.PublicPort ?? 0),
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
                Image = response.Config?.Image ?? string.Empty,
                State = MapState(response.State?.Status ?? "unknown"),
                CreatedAt = new DateTimeOffset(response.Created),
                Labels = response.Config?.Labels != null ? new Dictionary<string, string>(response.Config.Labels) : new Dictionary<string, string>(),
                EnvironmentVariables = ParseEnvironmentVariables(response.Config?.Env),
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

        using var logStream = await client.Containers.GetContainerLogsAsync(containerId, logParams, cancellationToken);
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

        // Aspire/DCP parity: when the caller did not request any networks and the orchestrator
        // is configured to use a default network, auto-create (if missing) a shared session
        // network and attach the container to it with the container name as a DNS alias. This
        // mirrors how .NET Aspire's DCP creates a single 'aspire-session-network-*' per session
        // and joins every container resource to it so they can resolve each other by name.
        await EnsureDefaultNetworkAttachmentAsync(request, containerName, cancellationToken);

        // Validate that all requested user-defined networks exist before attempting to create the container.
        // This produces a clearer error than the Docker daemon's generic 404 on container create.
        var existingNetworks = await EnsureNetworksExistAsync(request.Networks, cancellationToken);

        var primaryNetwork = request.Networks?.FirstOrDefault();
        var primaryIsSpecialMode = primaryNetwork != null && IsSpecialNetworkMode(primaryNetwork.NetworkName);

        // Networking strategy — match what `docker run --network <name> [--network <name2> ...]` does:
        //
        // 1. For special modes (host/none/container:*) we set HostConfig.NetworkMode at create time;
        //    these cannot be attached via post-create ConnectAsync.
        // 2. For user-defined networks we set BOTH HostConfig.NetworkMode = <primary> AND
        //    NetworkingConfig.EndpointsConfig[<primary>] at create time. This is what the Docker CLI
        //    does and is the only reliable way to ensure the container actually lands on the requested
        //    network on all runtimes (Docker Engine, Docker Desktop, Rancher Desktop, Podman). Without
        //    EndpointsConfig at create time, the daemon attaches to the default 'bridge', and a
        //    subsequent ConnectAsync may end up showing the container on bridge in some inspect paths.
        // 3. Any additional (secondary) networks are attached via ConnectAsync after creation, since
        //    Docker only allows a single endpoint to be specified at create time.
        NetworkingConfig? networkingConfig = null;
        string? networkMode = null;

        if (primaryNetwork != null)
        {
            if (primaryIsSpecialMode)
            {
                networkMode = primaryNetwork.NetworkName;
            }
            else
            {
                // For custom networks, use the ID as the key and also set it in EndpointSettings.
                // This follows the recommended pattern for Docker.DotNet and ensures compatibility.
                var resolvedPrimary = existingNetworks.FirstOrDefault(n =>
                    string.Equals(n.Id, primaryNetwork.NetworkName, StringComparison.Ordinal) ||
                    string.Equals(n.Name, primaryNetwork.NetworkName, StringComparison.Ordinal));
                var primaryId = resolvedPrimary?.Id ?? primaryNetwork.NetworkName;
                var primaryName = resolvedPrimary?.Name ?? primaryNetwork.NetworkName;

                var primaryAliases = CanUseAliases(primaryName, primaryNetwork.Aliases)
                    ? primaryNetwork.Aliases.ToList()
                    : null;
var networkInfo = existingNetworks.FirstOrDefault(n =>n.Id == primaryId);
                networkingConfig = new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [primaryName] = new EndpointSettings
                        {
                            NetworkID = primaryId,
                            EndpointID = Guid.NewGuid().ToString(), // Docker will ignore this and generate its own ID, but it must be set to a non-empty value to be included in the create request
                            Aliases = primaryAliases ?? new List<string>()
                        }
                    }
                };
                
                logger.LogDebug("Configuring primary network {NetworkId} ({NetworkName}) with aliases {Aliases}", 
                    LogSanitizer.Sanitize(primaryId),
                    LogSanitizer.Sanitize(primaryName), 
                    primaryAliases != null ? string.Join(", ", primaryAliases) : "none");
            }
        }

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
            },
            NetworkingConfig = networkingConfig
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

    private async Task EnsureDefaultNetworkAttachmentAsync(
        CreateContainerRequest request,
        string? containerName,
        CancellationToken cancellationToken)
    {
        if (!orchestratorOptions.UseDefaultNetwork)
        {
            return;
        }

        // Only fall back to the default network when the caller did not specify any.
        // If the user explicitly chose networks (including special modes like 'host'),
        // respect that choice and do not silently add a second one.
        if (request.Networks is { Count: > 0 })
        {
            return;
        }

        var defaultNetwork = orchestratorOptions.ResolveDefaultNetworkName();
        if (string.IsNullOrWhiteSpace(defaultNetwork))
        {
            return;
        }

        // Auto-create the network if it doesn't exist yet (idempotent).
        var existing = await networkManager.ListAsync(cancellationToken);
        if (!existing.Any(n =>
                string.Equals(n.Name, defaultNetwork, StringComparison.Ordinal) ||
                string.Equals(n.Id, defaultNetwork, StringComparison.Ordinal)))
        {
            logger.LogInformation("Auto-creating default orchestrator network {Network}",
                LogSanitizer.Sanitize(defaultNetwork));
            await networkManager.CreateAsync(defaultNetwork, cancellationToken: cancellationToken);
        }

        // DCP parity: register the container's name as a DNS alias on the shared
        // network so other containers on the same network can resolve it by name.
        var aliases = new List<string>();
        if (!string.IsNullOrWhiteSpace(containerName))
        {
            aliases.Add(containerName);
        }

        request.Networks ??= new List<Models.NetworkAttachment>();
        request.Networks.Add(new Models.NetworkAttachment
        {
            NetworkName = defaultNetwork,
            Aliases = aliases
        });
    }

    private async Task<IReadOnlyList<NetworkInfo>> EnsureNetworksExistAsync(IList<Models.NetworkAttachment>? networks, CancellationToken cancellationToken)
    {
        var existing = await networkManager.ListAsync(cancellationToken);
        if (networks == null || networks.Count == 0)
        {
            return existing;
        }

        // Special modes (host/none/container:*) are not real networks and don't need to exist.
        var toCheck = networks
            .Select(n => n.NetworkName)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !IsSpecialNetworkMode(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (toCheck.Count == 0)
        {
            return existing;
        }

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

        return existing;
    }

    private static bool IsSpecialNetworkMode(string networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName))
        {
            return false;
        }

        return networkName.Equals("host", StringComparison.OrdinalIgnoreCase)
               || networkName.Equals("none", StringComparison.OrdinalIgnoreCase)
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
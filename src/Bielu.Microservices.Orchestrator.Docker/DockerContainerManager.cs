using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using DockerRestartPolicy = Docker.DotNet.Models.RestartPolicy;
using OrchestratorRestartPolicy = Bielu.Microservices.Orchestrator.Models.RestartPolicy;

namespace Bielu.Microservices.Orchestrator.Docker;

/// <summary>
/// Docker implementation of the container manager.
/// </summary>
public class DockerContainerManager(
    DockerClient client,
    OrchestratorOptions orchestratorOptions,
    IImageManager imageManager,
    INetworkManager networkManager,
    IVolumeManager volumeManager,
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
            }).ToList() ?? new List<PortMapping>(),
            Volumes = c.Mounts?.Select(m => new VolumeMount
            {
                HostPath = string.IsNullOrEmpty(m.Source) ? m.Name ?? string.Empty : m.Source,
                ContainerPath = m.Destination ?? string.Empty,
                ReadOnly = !m.RW
            }).ToList() ?? new List<VolumeMount>()
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
                Volumes = response.Mounts?.Select(m => new VolumeMount
                {
                    HostPath = string.IsNullOrEmpty(m.Source) ? m.Name ?? string.Empty : m.Source,
                    ContainerPath = m.Destination ?? string.Empty,
                    ReadOnly = !m.RW
                }).ToList() ?? new List<VolumeMount>()
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

    public async Task RemoveAsync(string containerId, bool force = false, bool removeVolumes = false, CancellationToken cancellationToken = default)
    {
        IList<VolumeMount> mounts = [];
        if (removeVolumes)
        {
            var info = await GetAsync(containerId, cancellationToken);
            mounts = info?.Volumes ?? [];
        }

        await client.Containers.RemoveContainerAsync(containerId,
            new ContainerRemoveParameters { Force = force, RemoveVolumes = removeVolumes }, cancellationToken);

        if (removeVolumes)
        {
            foreach (var mount in mounts)
            {
                if (string.IsNullOrEmpty(mount.HostPath)) continue;

                if (mount.IsBindMount)
                {
                    try
                    {
                        await CleanBindMountAsync(mount.HostPath, cancellationToken);
                        logger.LogInformation("Cleaned bind-mount directory {Path} via helper container", mount.HostPath);
                    }
                    catch (Exception ex)
                    {
                        // Best-effort: container is already gone, log and continue.
                        logger.LogWarning(ex, "Failed to clean bind-mount directory {Path}", mount.HostPath);
                    }
                }
                else
                {
                    // Named volume — Docker's RemoveVolumes flag only removes anonymous ones.
                    try
                    {
                        await client.Volumes.RemoveAsync(mount.HostPath, force, cancellationToken);
                        logger.LogInformation("Removed named volume {Name}", LogSanitizer.Sanitize(mount.HostPath));
                    }
                    catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound
                                                        || ex.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        // Volume already gone or still referenced by another container — skip.
                        logger.LogDebug("Skipped volume {Name}: {Message}", LogSanitizer.Sanitize(mount.HostPath), ex.Message);
                    }
                }
            }
        }

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

        // Auto-create any local-driver bound volumes declared on the request
        await EnsureLocalBoundVolumesExistAsync(request, cancellationToken);

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
        var effectiveNetworks = await GetEffectiveNetworksAsync(request, containerName, cancellationToken);

        // Validate that all requested user-defined networks exist before attempting to create the container.
        // This produces a clearer error than the Docker daemon's generic 404 on container create.
        var existingNetworks = await EnsureNetworksExistAsync(effectiveNetworks, cancellationToken);

        var primaryNetwork = effectiveNetworks?.FirstOrDefault();
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

                // Build a sanitised attachment that strips aliases when they are not allowed
                // for the resolved network (e.g. the default 'bridge' does not support them),
                // and auto-populate gateway / driver options from the resolved network's
                // NetworkResponse data when the caller has not supplied them explicitly.
                var primaryAttachment = new Models.NetworkAttachment
                {
                    NetworkName = primaryNetwork.NetworkName,
                    Aliases = primaryAliases ?? new List<string>(),
                    IPv4Address = primaryNetwork.IPv4Address,
                    IPv6Address = primaryNetwork.IPv6Address,
                    Gateway = primaryNetwork.Gateway,
                    MacAddress = primaryNetwork.MacAddress,
                    Links = primaryNetwork.Links,
                    DriverOptions = primaryNetwork.DriverOptions,
                    DnsNames = primaryNetwork.DnsNames
                };
                EnrichFromNetworkInfo(primaryAttachment, resolvedPrimary);
                StripUnsupportedStaticAddressing(primaryAttachment, resolvedPrimary, primaryName);

                networkingConfig = new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [primaryId] = DockerNetworkManager.BuildEndpointSettings(primaryId, primaryAttachment)
                    }
                };

                networkMode = primaryId;

                logger.LogDebug("Configuring primary network {NetworkId} ({NetworkName}) with aliases {Aliases}, IPv4 {IPv4}, IPv6 {IPv6}, Gateway {Gateway}",
                    LogSanitizer.Sanitize(primaryId),
                    LogSanitizer.Sanitize(primaryName),
                    primaryAliases != null ? string.Join(", ", primaryAliases) : "none",
                    primaryAttachment.IPv4Address ?? "auto",
                    primaryAttachment.IPv6Address ?? "auto",
                    primaryAttachment.Gateway ?? "auto");
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
                Binds = request.Volumes.Select(v => v.ToBindString()).ToList(),
                AutoRemove = request.AutoRemove,
                NetworkMode = networkMode,
                RestartPolicy = new DockerRestartPolicy
                {
                    Name = request.RestartPolicy switch
                    {
                        OrchestratorRestartPolicy.Always        => RestartPolicyKind.Always,
                        OrchestratorRestartPolicy.UnlessStopped => RestartPolicyKind.UnlessStopped,
                        OrchestratorRestartPolicy.OnFailure     => RestartPolicyKind.OnFailure,
                        _                                       => RestartPolicyKind.No
                    },
                    MaximumRetryCount = request.MaxRestartRetries
                }
            },
            NetworkingConfig = networkingConfig
        };

        if (request.Command is { Count: > 0 })
        {
            createParams.Cmd = request.Command.ToList();
        }

        var response = await client.Containers.CreateContainerAsync(createParams, cancellationToken);

        // Attach additional networks (and re-attach primary to ensure aliases/config)
        if (effectiveNetworks != null)
        {
            foreach (var network in effectiveNetworks)
            {
                if (IsSpecialNetworkMode(network.NetworkName))
                {
                    continue;
                }

                var resolved = existingNetworks.FirstOrDefault(n =>
                    string.Equals(n.Id, network.NetworkName, StringComparison.Ordinal) ||
                    string.Equals(n.Name, network.NetworkName, StringComparison.Ordinal));
                var networkId = resolved?.Id ?? network.NetworkName;
                var networkName = resolved?.Name ?? network.NetworkName;

                var aliases = CanUseAliases(networkName, network.Aliases) ? network.Aliases : null;
                var connectAttachment = new Models.NetworkAttachment
                {
                    NetworkName = network.NetworkName,
                    Aliases = aliases?.ToList() ?? new List<string>(),
                    IPv4Address = network.IPv4Address,
                    IPv6Address = network.IPv6Address,
                    Gateway = network.Gateway,
                    MacAddress = network.MacAddress,
                    Links = network.Links,
                    DriverOptions = network.DriverOptions,
                    DnsNames = network.DnsNames
                };
                EnrichFromNetworkInfo(connectAttachment, resolved);
                StripUnsupportedStaticAddressing(connectAttachment, resolved, networkName);

                logger.LogInformation("Connecting container {ContainerId} to network {NetworkId} ({NetworkName})",
                    LogSanitizer.Sanitize(response.ID), LogSanitizer.Sanitize(networkId), LogSanitizer.Sanitize(networkName));
                await networkManager.ConnectAsync(networkId, response.ID, connectAttachment, cancellationToken);
            }
        }

        logger.LogInformation("Created container {ContainerId} from image {Image}",
            LogSanitizer.Sanitize(response.ID), LogSanitizer.Sanitize(request.Image));
        return response.ID;
    }

    private async Task<IList<Models.NetworkAttachment>?> GetEffectiveNetworksAsync(
        CreateContainerRequest request,
        string? containerName,
        CancellationToken cancellationToken)
    {
        if (request.Networks is { Count: > 0 })
        {
            return request.Networks;
        }

        if (!orchestratorOptions.UseDefaultNetwork)
        {
            return request.Networks;
        }

        var defaultNetwork = orchestratorOptions.ResolveDefaultNetworkName();
        if (string.IsNullOrWhiteSpace(defaultNetwork))
        {
            return request.Networks;
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

        return new List<Models.NetworkAttachment>
        {
            new Models.NetworkAttachment
            {
                NetworkName = defaultNetwork,
                Aliases = aliases
            }
        };
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

    /// <summary>
    /// Enriches a <see cref="Models.NetworkAttachment"/> with values that can be
    /// derived from the resolved network's <see cref="NetworkInfo"/> (mapped from
    /// <c>NetworkResponse</c>):
    /// <list type="bullet">
    ///   <item>the first IPAM gateway,</item>
    ///   <item>network-level driver options,</item>
    ///   <item>and — when no static IP was supplied — the first free IPv4 / IPv6
    ///         address computed from the network's IPAM subnet(s) and the set of
    ///         IPs already in use by attached endpoints.</item>
    /// </list>
    /// User-supplied values are always preserved and only missing fields are filled.
    /// </summary>
    private static void EnrichFromNetworkInfo(Models.NetworkAttachment attachment, NetworkInfo? info)
    {
        if (info == null)
        {
            return;
        }


        // Driver options: merge network-level options as defaults.
        if (info.Options is { Count: > 0 })
        {
            attachment.DriverOptions ??= new Dictionary<string, string>();
            foreach (var kv in info.Options)
            {
                if (!attachment.DriverOptions.ContainsKey(kv.Key))
                {
                    attachment.DriverOptions[kv.Key] = kv.Value;
                }
            }
        }

    }

    /// <summary>
    /// Defensively clears static IPv4/IPv6/Gateway values from a <see cref="Models.NetworkAttachment"/>
    /// when the resolved network does not have user-configured subnets in its IPAM
    /// config. Docker rejects user-supplied IP addresses on such networks with:
    ///   "user specified IP address is supported only when connecting to networks
    ///    with user configured subnets".
    /// This guards both create-time (<c>EndpointsConfig</c>) and connect-time
    /// (<c>ConnectNetwork</c>) flows so that callers who pass an explicit static IP
    /// for a network that does not allow it (e.g. Aspire's session network) still
    /// succeed — the IP is silently dropped and the daemon allocates one.
    /// </summary>
    private void StripUnsupportedStaticAddressing(Models.NetworkAttachment attachment, NetworkInfo? info, string networkName)
    {
        if (info == null || HasUserConfiguredSubnets(info.Ipam))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(attachment.IPv4Address)
            || !string.IsNullOrWhiteSpace(attachment.IPv6Address)
            || !string.IsNullOrWhiteSpace(attachment.Gateway))
        {
            logger.LogWarning(
                "Network {NetworkName} has no user-configured subnets; dropping static IPv4 {IPv4}, IPv6 {IPv6}, Gateway {Gateway} from endpoint settings to avoid Docker BadRequest.",
                LogSanitizer.Sanitize(networkName),
                attachment.IPv4Address ?? "<none>",
                attachment.IPv6Address ?? "<none>",
                attachment.Gateway ?? "<none>");

            attachment.IPv4Address = null;
            attachment.IPv6Address = null;
            attachment.Gateway = null;
        }
    }

    private static bool HasUserConfiguredSubnets(NetworkIpamInfo? ipam)
    {
        if (ipam?.Config is not { Count: > 0 })
        {
            return false;
        }

        return ipam.Config.Any(c => !string.IsNullOrWhiteSpace(c.Subnet));
    }

    // Runs a short-lived Alpine container to delete the contents of a bind-mount directory.
    // Using a helper container rather than Directory.Delete means this works correctly when the
    // orchestrator itself is running inside Docker (e.g. via socket mount on Docker Desktop / WSL2),
    // because Docker resolves the host path from the host's perspective, not the orchestrator's.
    private async Task CleanBindMountAsync(string hostPath, CancellationToken cancellationToken)
    {
        const string cleanupImage = "alpine";
        const string cleanupTag = "latest";

        // Ensure the image is available; pull if missing.
        try
        {
            await client.Images.InspectImageAsync($"{cleanupImage}:{cleanupTag}", cancellationToken);
        }
        catch (DockerImageNotFoundException)
        {
            logger.LogInformation("Pulling {Image}:{Tag} for bind-mount cleanup", cleanupImage, cleanupTag);
            await imageManager.PullAsync(
                new PullImageRequest { Image = cleanupImage, Tag = cleanupTag }, cancellationToken);
        }

        var created = await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = $"{cleanupImage}:{cleanupTag}",
            // find + delete removes all contents (including hidden files) without touching the mount point itself.
            Cmd = ["find", "/cleanup", "-mindepth", "1", "-delete"],
            HostConfig = new HostConfig
            {
                Binds = [$"{hostPath}:/cleanup"],
                AutoRemove = false
            }
        }, cancellationToken);

        try
        {
            await client.Containers.StartContainerAsync(created.ID, null, cancellationToken);
            var result = await client.Containers.WaitContainerAsync(created.ID, cancellationToken);
            if (result.StatusCode != 0)
            {
                logger.LogWarning(
                    "Bind-mount cleanup container exited with code {Code} for path {Path}",
                    result.StatusCode, hostPath);
            }
        }
        finally
        {
            await client.Containers.RemoveContainerAsync(
                created.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);
        }
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

    private async Task EnsureLocalBoundVolumesExistAsync(
        CreateContainerRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var mount in request.Volumes)
        {
            if (mount.LocalDriverOptions is not { } opts)
            {
                continue;
            }

            var existing = await volumeManager.GetAsync(mount.HostPath, cancellationToken);
            if (existing != null)
            {
                logger.LogDebug("Local-bound volume {VolumeName} already exists, skipping creation",
                    LogSanitizer.Sanitize(mount.HostPath));
                continue;
            }

            logger.LogInformation("Auto-creating local-bound volume {VolumeName} bound to {Device}",
                LogSanitizer.Sanitize(mount.HostPath),
                LogSanitizer.Sanitize(opts.Device));

            await volumeManager.CreateAsync(
                mount.HostPath,
                "local",
                opts.ToDictionary(),
                cancellationToken);
        }
    }
}
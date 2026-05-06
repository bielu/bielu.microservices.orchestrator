using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Docker;

/// <summary>
/// Docker implementation of the network manager.
/// </summary>
public class DockerNetworkManager(
    DockerClient client,
    ILogger<DockerNetworkManager> logger) : INetworkManager
{

    public async Task<IReadOnlyList<NetworkInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var networks = await client.Networks.ListNetworksAsync(cancellationToken: cancellationToken);

        return networks.Select(MapToNetworkInfo).ToList().AsReadOnly();
    }

    private static NetworkInfo MapToNetworkInfo(NetworkResponse n) => new()
    {
        Id = n.ID,
        Name = n.Name,
        Driver = n.Driver,
        Scope = n.Scope ?? string.Empty,
        Created = n.Created == default ? null : n.Created,
        EnableIPv4 = n.EnableIPv4,
        EnableIPv6 = n.EnableIPv6,
        Internal = n.Internal,
        Attachable = n.Attachable,
        Ingress = n.Ingress,
        ConfigOnly = n.ConfigOnly,
        Ipam = MapIpam(n.IPAM),
        Options = n.Options != null
            ? new Dictionary<string, string>(n.Options)
            : new Dictionary<string, string>(),
        Labels = n.Labels != null
            ? new Dictionary<string, string>(n.Labels)
            : new Dictionary<string, string>(),
        Containers = MapContainers(n.Containers)
    };

    private static IDictionary<string, NetworkEndpointInfo> MapContainers(IDictionary<string, EndpointResource>? containers)
    {
        var result = new Dictionary<string, NetworkEndpointInfo>();
        if (containers == null)
        {
            return result;
        }

        foreach (var kv in containers)
        {
            if (kv.Value == null)
            {
                continue;
            }

            result[kv.Key] = new NetworkEndpointInfo
            {
                Name = kv.Value.Name ?? string.Empty,
                EndpointId = kv.Value.EndpointID ?? string.Empty,
                MacAddress = kv.Value.MacAddress ?? string.Empty,
                IPv4Address = kv.Value.IPv4Address ?? string.Empty,
                IPv6Address = kv.Value.IPv6Address ?? string.Empty
            };
        }

        return result;
    }

    private static NetworkIpamInfo? MapIpam(IPAM? ipam)
    {
        if (ipam == null)
        {
            return null;
        }

        return new NetworkIpamInfo
        {
            Driver = ipam.Driver ?? string.Empty,
            Options = ipam.Options != null
                ? new Dictionary<string, string>(ipam.Options)
                : new Dictionary<string, string>(),
            Config = ipam.Config?.Select(c => new NetworkIpamConfig
            {
                Subnet = c.Subnet ?? string.Empty,
                IPRange = c.IPRange ?? string.Empty,
                Gateway = c.Gateway ?? string.Empty,
                AuxAddress = c.AuxAddress != null
                    ? new Dictionary<string, string>(c.AuxAddress)
                    : new Dictionary<string, string>()
            }).ToList() ?? new List<NetworkIpamConfig>()
        };
    }

    public async Task<string> CreateAsync(string name, string driver = "bridge", CancellationToken cancellationToken = default)
    {
        var response = await client.Networks.CreateNetworkAsync(
            new NetworksCreateParameters { Name = name, Driver = driver }, cancellationToken);
        logger.LogInformation("Created network {NetworkId} with name {Name}", response.ID, name);
        return response.ID;
    }

    public async Task RemoveAsync(string networkId, CancellationToken cancellationToken = default)
    {
        await client.Networks.DeleteNetworkAsync(networkId, cancellationToken);
        logger.LogInformation("Removed network {NetworkId}", networkId);
    }

    public Task ConnectAsync(string networkId, string containerId, IEnumerable<string>? aliases = null, CancellationToken cancellationToken = default)
    {
        var attachment = new Models.NetworkAttachment
        {
            NetworkName = networkId,
            Aliases = aliases?.ToList() ?? new List<string>()
        };
        return ConnectAsync(networkId, containerId, attachment, cancellationToken);
    }

    public async Task ConnectAsync(string networkId, string containerId, Models.NetworkAttachment attachment, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
            var isConnected = container?.NetworkSettings?.Networks != null &&
                              (container.NetworkSettings.Networks.ContainsKey(networkId) ||
                               container.NetworkSettings.Networks.Values.Any(n => string.Equals(n.NetworkID, networkId, StringComparison.OrdinalIgnoreCase)));

            if (isConnected)
            {
                logger.LogDebug("Container {ContainerId} already connected to network {NetworkId}", containerId, networkId);
                return;
            }

            var endpoint = BuildEndpointSettings(networkId, attachment);
            await client.Networks.ConnectNetworkAsync(networkId,
                new NetworkConnectParameters
                {
                    Container = containerId,
                    EndpointConfig = endpoint
                }, cancellationToken);
            logger.LogInformation("Connected container {ContainerId} to network {NetworkId} with aliases {Aliases}, IPv4 {IPv4}, IPv6 {IPv6}",
                containerId, networkId,
                endpoint.Aliases.Count > 0 ? string.Join(", ", endpoint.Aliases) : "none",
                endpoint.IPAMConfig?.IPv4Address ?? "auto",
                endpoint.IPAMConfig?.IPv6Address ?? "auto");
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Already connected, this is fine
            logger.LogDebug("Container {ContainerId} already connected to network {NetworkId}", containerId, networkId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect container {ContainerId} to network {NetworkId}", containerId, networkId);
            throw;
        }
    }

    /// <summary>
    /// Builds an <see cref="EndpointSettings"/> from a <see cref="NetworkAttachment"/>,
    /// mapping aliases, static IPv4/IPv6 addresses, MAC address, gateway, links,
    /// driver options and DNS names.
    /// </summary>
    internal static EndpointSettings BuildEndpointSettings(string networkId, Models.NetworkAttachment? attachment)
    {
        var endpoint = new EndpointSettings
        {
            NetworkID = networkId,
            Aliases = attachment?.Aliases?.ToList() ?? new List<string>()
        };

        if (attachment == null)
        {
            return endpoint;
        }

        var hasStaticAddress = !string.IsNullOrWhiteSpace(attachment.IPv4Address)
                               || !string.IsNullOrWhiteSpace(attachment.IPv6Address);
        if (hasStaticAddress)
        {
            endpoint.IPAMConfig = new EndpointIPAMConfig
            {
                IPv4Address = attachment.IPv4Address ?? string.Empty,
                IPv6Address = attachment.IPv6Address ?? string.Empty
            };
        }

        if (!string.IsNullOrWhiteSpace(attachment.IPv4Address))
        {
            endpoint.IPAddress = attachment.IPv4Address!;
        }

        if (!string.IsNullOrWhiteSpace(attachment.IPv6Address))
        {
            endpoint.GlobalIPv6Address = attachment.IPv6Address!;
        }

        if (!string.IsNullOrWhiteSpace(attachment.Gateway))
        {
            endpoint.Gateway = attachment.Gateway!;
        }

        if (!string.IsNullOrWhiteSpace(attachment.MacAddress))
        {
            endpoint.MacAddress = attachment.MacAddress!;
        }

        if (attachment.Links is { Count: > 0 })
        {
            endpoint.Links = attachment.Links.ToList();
        }

        if (attachment.DriverOptions is { Count: > 0 })
        {
            endpoint.DriverOpts = new Dictionary<string, string>((IDictionary<string, string>)attachment.DriverOptions);
        }

        if (attachment.DnsNames is { Count: > 0 })
        {
            endpoint.DNSNames = attachment.DnsNames.ToList();
        }

        return endpoint;
    }

    public async Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        await client.Networks.DisconnectNetworkAsync(networkId,
            new NetworkDisconnectParameters { Container = containerId }, cancellationToken);
        logger.LogInformation("Disconnected container {ContainerId} from network {NetworkId}", containerId, networkId);
    }
}

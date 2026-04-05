using System.Text.Json;
using System.Text.Json.Nodes;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the network manager backed by CNI configuration files.
/// </summary>
/// <remarks>
/// <para>
/// <b>CNI is required.</b> containerd delegates all networking to the
/// <see href="https://github.com/containernetworking/cni">Container Network Interface (CNI)</see>
/// plugin framework. This implementation reads and writes CNI configuration files stored in
/// <see cref="ContainerdOptions.CniConfigPath"/> (default <c>/etc/cni/net.d</c>).
/// </para>
/// <para>
/// Before using any network operations you must install CNI plugins on the host, e.g.:
/// <code>
///   apt install containernetworking-plugins   # Debian / Ubuntu
///   dnf install containernetworking-plugins   # RHEL / Fedora
/// </code>
/// or download binaries from
/// <see href="https://github.com/containernetworking/plugins/releases"/>.
/// </para>
/// <para>
/// <b>Connect / Disconnect</b> are not exposed as a discrete API operation in CNI. Network
/// attachment happens at task-creation time by passing the network namespace to the CNI plugin.
/// Use the container's task lifecycle (via <see cref="IContainerManager"/>) to control
/// which networks a container is attached to.
/// </para>
/// </remarks>
public class ContainerdNetworkManager(
    ContainerdOptions options,
    ILogger<ContainerdNetworkManager> logger) : INetworkManager
{
    private static readonly string[] ConfigExtensions = [".conf", ".conflist", ".json"];

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true
    };

    // CNI config files are sorted lexicographically; a numeric prefix controls execution order.
    private const string DefaultCniPriority = "10";

    /// <inheritdoc />
    public Task<IReadOnlyList<NetworkInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing CNI networks from {CniConfigPath}", options.CniConfigPath);

        if (!Directory.Exists(options.CniConfigPath))
        {
            logger.LogDebug("CNI config directory {CniConfigPath} does not exist; returning empty list", options.CniConfigPath);
            return Task.FromResult<IReadOnlyList<NetworkInfo>>(new List<NetworkInfo>().AsReadOnly());
        }

        var networks = new List<NetworkInfo>();

        foreach (var file in Directory.EnumerateFiles(options.CniConfigPath)
                     .Where(f => ConfigExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                     .OrderBy(f => f))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var content = File.ReadAllText(file);
                var node = JsonNode.Parse(content);
                if (node is null) continue;

                // Both .conf (single plugin) and .conflist (plugin list) expose a top-level "name" field.
                var name = node["name"]?.GetValue<string>();
                var type = node["type"]?.GetValue<string>()
                           ?? node["plugins"]?[0]?["type"]?.GetValue<string>()
                           ?? "unknown";

                if (string.IsNullOrEmpty(name)) continue;

                networks.Add(new NetworkInfo
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    Name = name,
                    Driver = type,
                    Labels = new Dictionary<string, string>
                    {
                        ["cni.config.file"] = file
                    }
                });
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogWarning(ex, "Failed to parse CNI config file {File}; skipping", file);
            }
        }

        return Task.FromResult<IReadOnlyList<NetworkInfo>>(networks.AsReadOnly());
    }

    /// <inheritdoc />
    /// <remarks>
    /// Creates a CNI bridge network configuration file in <see cref="ContainerdOptions.CniConfigPath"/>.
    /// The <paramref name="driver"/> parameter maps to the CNI plugin type (e.g. <c>bridge</c>,
    /// <c>macvlan</c>, <c>ipvlan</c>). The corresponding plugin binary must exist in
    /// <see cref="ContainerdOptions.CniBinPath"/>.
    /// </remarks>
    public Task<string> CreateAsync(string name, string driver = "bridge", CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating CNI network {Name} (type={Driver}) in {CniConfigPath}",
            LogSanitizer.Sanitize(name), LogSanitizer.Sanitize(driver), options.CniConfigPath);

        Directory.CreateDirectory(options.CniConfigPath);

        var configFileName = $"{DefaultCniPriority}-{name}.conf";
        var configFilePath = Path.Combine(options.CniConfigPath, configFileName);

        var config = new JsonObject
        {
            ["cniVersion"] = "1.0.0",
            ["name"] = name,
            ["type"] = driver,
            ["bridge"] = $"cni-{name}",
            ["isGateway"] = true,
            ["ipMasq"] = true,
            ["ipam"] = new JsonObject
            {
                ["type"] = "host-local",
                ["ranges"] = new JsonArray
                {
                    new JsonArray { new JsonObject { ["subnet"] = options.CniDefaultSubnet } }
                },
                ["routes"] = new JsonArray
                {
                    new JsonObject { ["dst"] = "0.0.0.0/0" }
                }
            }
        };

        File.WriteAllText(configFilePath, config.ToJsonString(JsonWriteOptions));

        logger.LogInformation("Created CNI network config {ConfigFile}", configFilePath);

        return Task.FromResult(Path.GetFileNameWithoutExtension(configFileName));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Removes the CNI configuration file associated with the given network identifier
    /// (the file base-name returned by <see cref="CreateAsync"/> or the
    /// <see cref="NetworkInfo.Id"/> from <see cref="ListAsync"/>).
    /// </remarks>
    public Task RemoveAsync(string networkId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Removing CNI network {NetworkId}", LogSanitizer.Sanitize(networkId));

        if (!Directory.Exists(options.CniConfigPath))
        {
            logger.LogDebug("CNI config directory does not exist; nothing to remove for {NetworkId}", LogSanitizer.Sanitize(networkId));
            return Task.CompletedTask;
        }

        // Match by file base-name (Id) or by "name" field inside the config.
        var deleted = false;
        foreach (var file in Directory.EnumerateFiles(options.CniConfigPath)
                     .Where(f => ConfigExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
        {
            var baseName = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(baseName, networkId, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                logger.LogInformation("Deleted CNI config file {File}", file);
                deleted = true;
                continue;
            }

            // Also try matching the "name" field inside the config
            try
            {
                var content = File.ReadAllText(file);
                var node = JsonNode.Parse(content);
                var configName = node?["name"]?.GetValue<string>();
                if (string.Equals(configName, networkId, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    logger.LogInformation("Deleted CNI config file {File}", file);
                    deleted = true;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogWarning(ex, "Failed to inspect CNI config file {File} during removal", file);
            }
        }

        if (!deleted)
        {
            logger.LogDebug("No CNI config file found for network {NetworkId}", LogSanitizer.Sanitize(networkId));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Not supported. In CNI, a container is attached to a network when its task is created
    /// by passing the network namespace path to the CNI plugin binary. There is no separate
    /// "connect" step after the task is running. Use <see cref="IContainerManager.StartAsync"/>
    /// to start a container that is already associated with the desired network namespace.
    /// </remarks>
    public Task ConnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "CNI does not support attaching a running container to a network after start. " +
            "Network attachment in CNI happens at task-creation time via the network namespace. " +
            "Re-create the container with the desired network configuration.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Not supported. CNI network detachment from a running container is not exposed as a
    /// discrete API operation. The network namespace is released when the container task exits.
    /// </remarks>
    public Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "CNI does not support detaching a running container from a network. " +
            "The network namespace is released when the container task exits.");
    }
}

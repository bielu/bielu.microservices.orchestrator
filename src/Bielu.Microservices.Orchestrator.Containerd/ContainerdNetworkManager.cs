using System.Text.Json;
using System.Text.Json.Nodes;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Containerd.Services.Tasks.V1;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the network manager backed by CNI configuration files and
/// CNI plugin binary invocations.
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
/// <b>Connect / Disconnect</b> invoke CNI plugin binaries using the
/// <see href="https://github.com/containernetworking/cni/blob/main/SPEC.md">CNI wire protocol</see>
/// (the same mechanism used by <see href="https://github.com/containerd/go-cni">go-cni</see>).
/// The container's network namespace is resolved from its running task PID via
/// <c>/proc/{pid}/ns/net</c>, so the container must have a running task before calling
/// these methods.
/// </para>
/// </remarks>
public class ContainerdNetworkManager(
    Tasks.TasksClient tasksClient,
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
    /// Invokes the CNI <c>ADD</c> command for the specified network, wiring the network interface
    /// into the container's network namespace. The container must have a running task so that its
    /// PID—and therefore its network namespace at <c>/proc/{pid}/ns/net</c>—can be resolved.
    /// </remarks>
    public async Task ConnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Connecting container {ContainerId} to CNI network {NetworkId} (ADD)",
            LogSanitizer.Sanitize(containerId), LogSanitizer.Sanitize(networkId));

        var configJson = await FindConfigContentAsync(networkId, cancellationToken);
        var netns = await GetNetnsPathAsync(containerId, cancellationToken);

        var invoker = new CniPluginInvoker(logger);
        await invoker.AddAsync(containerId, netns, configJson, options.CniBinPath, cancellationToken);

        logger.LogInformation(
            "Connected container {ContainerId} to CNI network {NetworkId}",
            LogSanitizer.Sanitize(containerId), LogSanitizer.Sanitize(networkId));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Invokes the CNI <c>DEL</c> command for the specified network, removing the network
    /// interface from the container's network namespace. The container must have a running task.
    /// </remarks>
    public async Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Disconnecting container {ContainerId} from CNI network {NetworkId} (DEL)",
            LogSanitizer.Sanitize(containerId), LogSanitizer.Sanitize(networkId));

        var configJson = await FindConfigContentAsync(networkId, cancellationToken);
        var netns = await GetNetnsPathAsync(containerId, cancellationToken);

        var invoker = new CniPluginInvoker(logger);
        await invoker.DeleteAsync(containerId, netns, configJson, options.CniBinPath, cancellationToken);

        logger.LogInformation(
            "Disconnected container {ContainerId} from CNI network {NetworkId}",
            LogSanitizer.Sanitize(containerId), LogSanitizer.Sanitize(networkId));
    }

    /// <summary>
    /// Reads the raw CNI config JSON for a network identified by <paramref name="networkId"/>.
    /// <para>
    /// The <paramref name="networkId"/> may be either the file base-name (e.g. <c>10-mynet</c>)
    /// as returned by <see cref="ListAsync"/> / <see cref="CreateAsync"/>, or the logical
    /// <c>name</c> field inside the config file.
    /// </para>
    /// </summary>
    private Task<string> FindConfigContentAsync(string networkId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(options.CniConfigPath))
        {
            throw new DirectoryNotFoundException(
                $"CNI config directory '{options.CniConfigPath}' does not exist.");
        }

        foreach (var file in Directory.EnumerateFiles(options.CniConfigPath)
                     .Where(f => ConfigExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                     .OrderBy(f => f))
        {
            // Match by file base-name first (fast path).
            if (string.Equals(Path.GetFileNameWithoutExtension(file), networkId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(File.ReadAllText(file));
            }

            // Also check the "name" field inside the JSON.
            try
            {
                var content = File.ReadAllText(file);
                var node = JsonNode.Parse(content);
                var configName = node?["name"]?.GetValue<string>();
                if (string.Equals(configName, networkId, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(content);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogWarning(ex, "Failed to inspect CNI config file {File}", file);
            }
        }

        throw new KeyNotFoundException(
            $"No CNI config file found for network '{networkId}' in '{options.CniConfigPath}'.");
    }

    /// <summary>
    /// Resolves the network namespace path for a container's running task.
    /// The namespace is located at <c>/proc/{pid}/ns/net</c> where <c>pid</c> is the
    /// init process PID reported by containerd.
    /// </summary>
    private async Task<string> GetNetnsPathAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await tasksClient.GetAsync(
                new GetRequest { ContainerId = containerId },
                cancellationToken: cancellationToken);

            var pid = response.Process?.Pid
                      ?? throw new InvalidOperationException(
                          $"Task for container '{containerId}' has no PID.");

            if (pid == 0)
            {
                throw new InvalidOperationException(
                    $"Task for container '{containerId}' has PID 0; the task may not be running.");
            }

            return $"/proc/{pid}/ns/net";
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Container '{containerId}' has no running task. " +
                "Start the container before connecting it to a network.", ex);
        }
    }
}

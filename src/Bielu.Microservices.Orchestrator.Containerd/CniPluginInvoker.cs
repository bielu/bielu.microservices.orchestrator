using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// Invokes CNI plugin binaries using the
/// <see href="https://github.com/containernetworking/cni/blob/main/SPEC.md">CNI wire protocol</see>.
/// This mirrors what <see href="https://github.com/containerd/go-cni">go-cni</see> does internally:
/// the runtime sets environment variables and pipes the network config JSON to the plugin's stdin.
/// </summary>
/// <param name="logger">Logger for plugin invocations.</param>
/// <param name="runProcess">
/// Starts the plugin process, writes the config to its stdin and collects the result; defaults to
/// running the real binary. Tests substitute it to observe the CNI wire protocol without plugins.
/// </param>
internal sealed class CniPluginInvoker(ILogger logger, CniPluginInvoker.RunProcess? runProcess = null)
{
    /// <summary>Runs a plugin process to completion.</summary>
    internal delegate Task<ProcessResult> RunProcess(ProcessStartInfo startInfo, string stdin, CancellationToken cancellationToken);

    /// <summary>The exit code and captured output of a plugin run.</summary>
    internal readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);

    private readonly RunProcess _runProcess = runProcess ?? RunAsync;

    /// <summary>Default interface name assigned to the first network interface inside the container namespace.</summary>
    internal const string DefaultIfName = "eth0";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// Invokes the CNI <c>ADD</c> command for the given network config.
    /// For <c>.conf</c> (single plugin) files the single plugin binary is executed once.
    /// For <c>.conflist</c> files every plugin in the list is executed in order and
    /// the <c>prevResult</c> from each plugin is forwarded to the next.
    /// </summary>
    public async Task AddAsync(
        string containerId,
        string netns,
        string configJson,
        string cniBinPath,
        CancellationToken cancellationToken = default)
    {
        var root = JsonNode.Parse(configJson)
                   ?? throw new InvalidOperationException("CNI config JSON is invalid.");

        if (root["plugins"] is JsonArray)
        {
            await ExecuteConfListAsync("ADD", containerId, netns, root, cniBinPath, cancellationToken);
        }
        else
        {
            await InvokePluginAsync("ADD", containerId, netns, DefaultIfName, cniBinPath, configJson, cancellationToken);
        }
    }

    /// <summary>
    /// Invokes the CNI <c>DEL</c> command for the given network config.
    /// For <c>.conflist</c> files the plugins are invoked in <b>reverse</b> order (per spec).
    /// </summary>
    public async Task DeleteAsync(
        string containerId,
        string netns,
        string configJson,
        string cniBinPath,
        CancellationToken cancellationToken = default)
    {
        var root = JsonNode.Parse(configJson)
                   ?? throw new InvalidOperationException("CNI config JSON is invalid.");

        if (root["plugins"] is JsonArray)
        {
            await ExecuteConfListAsync("DEL", containerId, netns, root, cniBinPath, cancellationToken);
        }
        else
        {
            await InvokePluginAsync("DEL", containerId, netns, DefaultIfName, cniBinPath, configJson, cancellationToken);
        }
    }

    private async Task ExecuteConfListAsync(
        string command,
        string containerId,
        string netns,
        JsonNode conflist,
        string cniBinPath,
        CancellationToken cancellationToken)
    {
        var cniVersion = conflist["cniVersion"]?.GetValue<string>() ?? "1.0.0";
        var name = conflist["name"]?.GetValue<string>() ?? string.Empty;
        var plugins = (JsonArray)conflist["plugins"]!;

        // For DEL the spec requires invoking plugins in reverse order.
        IEnumerable<JsonNode?> ordered = command == "DEL"
            ? plugins.Reverse()
            : plugins.AsEnumerable();

        string? prevResultJson = null;

        foreach (var pluginNode in ordered)
        {
            if (pluginNode is null) continue;

            // Build per-plugin config: top-level fields merged with plugin block.
            var pluginConfig = new JsonObject
            {
                ["cniVersion"] = cniVersion,
                ["name"] = name
            };

            // Copy all fields from the plugin block into the per-plugin config.
            foreach (var prop in pluginNode.AsObject().ToList())
            {
                pluginConfig[prop.Key] = prop.Value?.DeepClone();
            }

            // Inject prevResult for ADD (first plugin has none).
            if (command == "ADD" && prevResultJson is not null)
            {
                pluginConfig["prevResult"] = JsonNode.Parse(prevResultJson);
            }

            var pluginJson = pluginConfig.ToJsonString(JsonOptions);
            prevResultJson = await InvokePluginAsync(
                command, containerId, netns, DefaultIfName, cniBinPath, pluginJson, cancellationToken);
        }
    }

    /// <summary>
    /// Locates and executes a single CNI plugin binary.
    /// </summary>
    /// <returns>
    /// The stdout of the plugin on success (may be <c>null</c> or empty for DEL).
    /// </returns>
    private async Task<string?> InvokePluginAsync(
        string command,
        string containerId,
        string netns,
        string ifname,
        string cniBinPath,
        string configJson,
        CancellationToken cancellationToken)
    {
        var pluginType = ParsePluginType(configJson)
                         ?? throw new InvalidOperationException(
                             "Cannot determine CNI plugin type from config: 'type' field is missing.");

        var binaryPath = FindPluginBinary(cniBinPath, pluginType)
                         ?? throw new FileNotFoundException(
                             $"CNI plugin binary '{pluginType}' not found in '{cniBinPath}'.", pluginType);

        logger.LogDebug(
            "CNI {Command}: container={ContainerId} netns={Netns} ifname={IfName} plugin={Plugin}",
            command, containerId, netns, ifname, pluginType);

        var psi = new ProcessStartInfo(binaryPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.Environment["CNI_COMMAND"] = command;
        psi.Environment["CNI_CONTAINERID"] = containerId;
        psi.Environment["CNI_NETNS"] = netns;
        psi.Environment["CNI_IFNAME"] = ifname;
        psi.Environment["CNI_PATH"] = cniBinPath;
        psi.Environment["CNI_ARGS"] = string.Empty;

        var (exitCode, stdout, stderr) = await _runProcess(psi, configJson, cancellationToken);

        if (exitCode != 0)
        {
            // CNI plugins report errors as JSON on stdout.
            var errorDetail = TryExtractCniError(stdout) ?? stderr;
            throw new InvalidOperationException(
                $"CNI plugin '{pluginType}' failed for command {command} (exit {exitCode}): {errorDetail}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            logger.LogDebug("CNI plugin {Plugin} {Command} stderr: {Stderr}", pluginType, command, stderr);
        }

        return string.IsNullOrWhiteSpace(stdout) ? null : stdout;
    }

    private static async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, string stdin, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Write config JSON to stdin and signal EOF.
        await using (var input = process.StandardInput)
        {
            await input.WriteAsync(stdin.AsMemory(), cancellationToken);
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string? ParsePluginType(string configJson)
    {
        try
        {
            var node = JsonNode.Parse(configJson);
            return node?["type"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindPluginBinary(string cniBinPath, string pluginType)
    {
        // pluginType is a simple name like "bridge" or "host-local" – no path separators expected.
        if (pluginType.Contains('/') || pluginType.Contains('\\') || pluginType.Contains('\0'))
        {
            return null;
        }

        var candidate = Path.Combine(cniBinPath, pluginType);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        return null;
    }

    private static string? TryExtractCniError(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return null;
        try
        {
            var node = JsonNode.Parse(stdout);
            var msg = node?["msg"]?.GetValue<string>();
            var details = node?["details"]?.GetValue<string>();
            if (string.IsNullOrEmpty(msg)) return null;
            return string.IsNullOrEmpty(details) ? msg : $"{msg}: {details}";
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

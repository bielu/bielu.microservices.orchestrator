using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Docker;

/// <summary>
/// Docker implementation of the container manager.
/// </summary>
public class DockerContainerManager : IContainerManager
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerContainerManager> _logger;

    public DockerContainerManager(DockerClient client, ILogger<DockerContainerManager> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListAsync(bool all = false, CancellationToken cancellationToken = default)
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters { All = all }, cancellationToken);

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
            var response = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
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
        var createParams = new CreateContainerParameters
        {
            Image = request.Image,
            Name = request.Name,
            Env = request.EnvironmentVariables.Select(kv => $"{kv.Key}={kv.Value}").ToList(),
            Labels = new Dictionary<string, string>(request.Labels),
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

        var response = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);
        _logger.LogInformation("Created container {ContainerId} from image {Image}", response.ID, request.Image);
        return response.ID;
    }

    public async Task StartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
        _logger.LogInformation("Started container {ContainerId}", containerId);
    }

    public async Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var stopParams = new ContainerStopParameters();
        if (timeout.HasValue)
        {
            stopParams.WaitBeforeKillSeconds = (uint)timeout.Value.TotalSeconds;
        }

        await _client.Containers.StopContainerAsync(containerId, stopParams, cancellationToken);
        _logger.LogInformation("Stopped container {ContainerId}", containerId);
    }

    public async Task RemoveAsync(string containerId, bool force = false, CancellationToken cancellationToken = default)
    {
        await _client.Containers.RemoveContainerAsync(containerId,
            new ContainerRemoveParameters { Force = force }, cancellationToken);
        _logger.LogInformation("Removed container {ContainerId}", containerId);
    }

    public async Task<string> GetLogsAsync(string containerId, bool stdout = true, bool stderr = true, CancellationToken cancellationToken = default)
    {
        var logParams = new ContainerLogsParameters
        {
            ShowStdout = stdout,
            ShowStderr = stderr
        };

        using var logStream = await _client.Containers.GetContainerLogsAsync(containerId, false, logParams, cancellationToken);
        using var memoryStream = new MemoryStream();
        await logStream.CopyOutputToAsync(Stream.Null, memoryStream, Stream.Null, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
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

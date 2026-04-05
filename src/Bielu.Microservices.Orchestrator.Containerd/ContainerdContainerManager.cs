using System.Text;
using System.Text.Json;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Utilities;
using Containerd.Services.Containers.V1;
using Containerd.Services.Snapshots.V1;
using Containerd.Services.Tasks.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using ContainerInfo = Bielu.Microservices.Orchestrator.Models.ContainerInfo;
using ContainerState = Bielu.Microservices.Orchestrator.Models.ContainerState;
using CreateContainerRequest = Bielu.Microservices.Orchestrator.Models.CreateContainerRequest;
using GrpcCreateContainerRequest = Containerd.Services.Containers.V1.CreateContainerRequest;
using TaskProcess = Containerd.V1.Types.Process;
using TaskStatus = Containerd.V1.Types.Status;
using ContainerdMount = Containerd.Types.Mount;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the container manager using gRPC.
/// </summary>
public class ContainerdContainerManager(
    Containers.ContainersClient containersClient,
    Tasks.TasksClient tasksClient,
    Snapshots.SnapshotsClient snapshotsClient,
    ContainerdOptions options,
    ILogger<ContainerdContainerManager> logger) : IContainerManager
{
    private const string DefaultSnapshotter = "overlayfs";
    private const string OciSpecVersion = "1.0.2";
    private const string DefaultContainerPath = "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

    // SIGTERM = 15, SIGKILL = 9
    private const uint SigTerm = 15;
    private const uint SigKill = 9;

    public async Task<IReadOnlyList<ContainerInfo>> ListAsync(bool all = false, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing containerd containers in namespace {Namespace}", LogSanitizer.Sanitize(options.Namespace));

        var headers = NamespaceHeader();
        var listResponse = await containersClient.ListAsync(new ListContainersRequest(), headers, cancellationToken: cancellationToken);

        // Build a lookup of task statuses
        var taskStatuses = await GetTaskStatusMapAsync(headers, cancellationToken);

        return listResponse.Containers
            .Select(c => MapContainer(c, taskStatuses.GetValueOrDefault(c.Id)))
            .ToList()
            .AsReadOnly();
    }

    public async Task<ContainerInfo?> GetAsync(string containerId, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting containerd container {ContainerId} in namespace {Namespace}",
            LogSanitizer.Sanitize(containerId), LogSanitizer.Sanitize(options.Namespace));

        try
        {
            var headers = NamespaceHeader();
            var response = await containersClient.GetAsync(
                new GetContainerRequest { Id = containerId }, headers, cancellationToken: cancellationToken);

            var process = await TryGetTaskProcessAsync(containerId, headers, cancellationToken);
            return MapContainer(response.Container, process);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
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
                ["orchestrator.group"] = groupName,
                ["orchestrator.replica-index"] = i.ToString()
            };

            var id = await CreateSingleContainerAsync(request, replicaName, cancellationToken, replicaLabels);
            firstId ??= id;
        }

        logger.LogInformation("Created {Replicas} containerd container replicas in group {GroupName}",
            request.Replicas, LogSanitizer.Sanitize(groupName));
        return firstId!;
    }

    public async Task StartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting containerd task for container {ContainerId}", LogSanitizer.Sanitize(containerId));

        var headers = NamespaceHeader();

        // Retrieve container details to get snapshot information
        var containerResponse = await containersClient.GetAsync(
            new GetContainerRequest { Id = containerId }, headers, cancellationToken: cancellationToken);
        var container = containerResponse.Container;

        // Get snapshot mounts so the task can use the container's filesystem
        var mounts = await GetSnapshotMountsAsync(
            container.SnapshotKey, container.Snapshotter, headers, cancellationToken);

        // Create the task (associates a running process with the container)
        var createTaskRequest = new CreateTaskRequest { ContainerId = containerId };
        createTaskRequest.Rootfs.AddRange(mounts);
        await tasksClient.CreateAsync(createTaskRequest, headers, cancellationToken: cancellationToken);

        // Start the task
        await tasksClient.StartAsync(
            new StartRequest { ContainerId = containerId }, headers, cancellationToken: cancellationToken);

        logger.LogInformation("Started containerd task for container {ContainerId}", LogSanitizer.Sanitize(containerId));
    }

    public async Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Stopping containerd task for container {ContainerId}", LogSanitizer.Sanitize(containerId));

        var headers = NamespaceHeader();

        try
        {
            // Send SIGTERM first for graceful shutdown
            await tasksClient.KillAsync(
                new KillRequest { ContainerId = containerId, Signal = SigTerm, All = true },
                headers, cancellationToken: cancellationToken);

            if (timeout.HasValue)
            {
                // Wait up to the timeout, then force kill if still running
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout.Value);
                try
                {
                    await tasksClient.WaitAsync(
                        new WaitRequest { ContainerId = containerId },
                        headers, cancellationToken: cts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout expired; escalate to SIGKILL
                    await tasksClient.KillAsync(
                        new KillRequest { ContainerId = containerId, Signal = SigKill, All = true },
                        headers, cancellationToken: cancellationToken);
                }
            }

            logger.LogInformation("Stopped containerd task for container {ContainerId}", LogSanitizer.Sanitize(containerId));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            logger.LogDebug("No running task found for container {ContainerId}", LogSanitizer.Sanitize(containerId));
        }
    }

    public async Task RemoveAsync(string containerId, bool force = false, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Removing containerd container {ContainerId}", LogSanitizer.Sanitize(containerId));

        var headers = NamespaceHeader();

        // Kill and delete any running task first
        try
        {
            await tasksClient.KillAsync(
                new KillRequest { ContainerId = containerId, Signal = SigKill, All = true },
                headers, cancellationToken: cancellationToken);

            await tasksClient.DeleteAsync(
                new DeleteTaskRequest { ContainerId = containerId },
                headers, cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // No task to clean up
        }

        // Remove the active snapshot if one exists
        try
        {
            var containerResponse = await containersClient.GetAsync(
                new GetContainerRequest { Id = containerId }, headers, cancellationToken: cancellationToken);
            var snapshotKey = containerResponse.Container.SnapshotKey;

            if (!string.IsNullOrEmpty(snapshotKey))
            {
                await snapshotsClient.RemoveAsync(
                    new RemoveSnapshotRequest { Key = snapshotKey },
                    headers, cancellationToken: cancellationToken);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // Snapshot may not exist
        }

        // Delete the container metadata
        await containersClient.DeleteAsync(
            new DeleteContainerRequest { Id = containerId },
            headers, cancellationToken: cancellationToken);

        logger.LogInformation("Removed containerd container {ContainerId}", LogSanitizer.Sanitize(containerId));
    }

    /// <inheritdoc />
    public Task<string> GetLogsAsync(string containerId, bool stdout = true, bool stderr = true, CancellationToken cancellationToken = default)
    {
        // containerd writes task stdio to FIFO files on the host; there is no gRPC log-retrieval API.
        throw new NotSupportedException(
            "containerd does not expose container logs over gRPC. " +
            "Read the FIFO/log files directly on the host, or use a log forwarding agent.");
    }

    /// <inheritdoc />
    public Task ScaleAsync(string containerId, int replicas, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "containerd does not natively support scaling. " +
            "Use CreateAsync with Replicas > 1 to create multiple container instances.");
    }

    private async Task<string> CreateSingleContainerAsync(
        CreateContainerRequest request,
        string? containerName,
        CancellationToken cancellationToken,
        Dictionary<string, string>? overrideLabels = null)
    {
        var containerId = containerName ?? $"orchestrator-{Guid.NewGuid():N}";
        var snapshotKey = containerId;
        var headers = NamespaceHeader();

        var labels = overrideLabels ?? new Dictionary<string, string>(request.Labels);

        var specJson = BuildOciSpec(request);
        var spec = new Any
        {
            TypeUrl = "types.containerd.io/opencontainers/runtime-spec/1/linux",
            Value = ByteString.CopyFrom(Encoding.UTF8.GetBytes(specJson))
        };

        var container = new Container
        {
            Id = containerId,
            Image = request.Image,
            Snapshotter = DefaultSnapshotter,
            SnapshotKey = snapshotKey,
            Spec = spec,
            Runtime = new Container.Types.Runtime { Name = "io.containerd.runc.v2" }
        };

        foreach (var label in labels)
        {
            container.Labels[label.Key] = label.Value;
        }

        var createRequest = new GrpcCreateContainerRequest { Container = container };
        var response = await containersClient.CreateAsync(createRequest, headers, cancellationToken: cancellationToken);

        logger.LogInformation("Created containerd container {ContainerId} from image {Image} in namespace {Namespace}",
            LogSanitizer.Sanitize(response.Container.Id), LogSanitizer.Sanitize(request.Image), LogSanitizer.Sanitize(options.Namespace));

        return response.Container.Id;
    }

    private async Task<Dictionary<string, TaskProcess>> GetTaskStatusMapAsync(Metadata headers, CancellationToken cancellationToken)
    {
        try
        {
            var tasksResponse = await tasksClient.ListAsync(new ListTasksRequest(), headers, cancellationToken: cancellationToken);
            return tasksResponse.Tasks.ToDictionary(t => t.ContainerId, t => t);
        }
        catch (RpcException ex)
        {
            logger.LogDebug(ex, "Failed to retrieve task list for state mapping");
            return new Dictionary<string, TaskProcess>();
        }
    }

    private async Task<TaskProcess?> TryGetTaskProcessAsync(string containerId, Metadata headers, CancellationToken cancellationToken)
    {
        try
        {
            var response = await tasksClient.GetAsync(
                new GetRequest { ContainerId = containerId }, headers, cancellationToken: cancellationToken);
            return response.Process;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<IEnumerable<ContainerdMount>> GetSnapshotMountsAsync(
        string snapshotKey,
        string snapshotter,
        Metadata headers,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(snapshotKey))
        {
            return Enumerable.Empty<ContainerdMount>();
        }

        try
        {
            var mountsResponse = await snapshotsClient.MountsAsync(
                new MountsRequest
                {
                    Key = snapshotKey,
                    Snapshotter = string.IsNullOrEmpty(snapshotter) ? DefaultSnapshotter : snapshotter
                },
                headers, cancellationToken: cancellationToken);

            return mountsResponse.Mounts;
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Failed to retrieve mounts for snapshot {SnapshotKey}; task will be created with no rootfs mounts",
                LogSanitizer.Sanitize(snapshotKey));
            return Enumerable.Empty<ContainerdMount>();
        }
    }

    private static ContainerInfo MapContainer(Container container, TaskProcess? process)
    {
        return new ContainerInfo
        {
            Id = container.Id,
            Name = container.Id,
            Image = container.Image,
            State = MapStatus(process?.Status),
            CreatedAt = container.CreatedAt != null
                ? DateTimeOffset.FromUnixTimeSeconds(container.CreatedAt.Seconds)
                : DateTimeOffset.MinValue,
            Labels = new Dictionary<string, string>(container.Labels)
        };
    }

    private static ContainerState MapStatus(TaskStatus? status)
    {
        return status switch
        {
            TaskStatus.Created => ContainerState.Created,
            TaskStatus.Running => ContainerState.Running,
            TaskStatus.Stopped => ContainerState.Exited,
            TaskStatus.Paused => ContainerState.Paused,
            _ => ContainerState.Unknown
        };
    }

    private static string BuildOciSpec(CreateContainerRequest request)
    {
        var args = request.Command is { Count: > 0 }
            ? request.Command.ToList()
            : new List<string> { "sh" };

        var env = request.EnvironmentVariables
            .Select(kv => $"{kv.Key}={kv.Value}")
            .Prepend(DefaultContainerPath)
            .ToList();

        var bindMounts = request.Volumes.Select(v =>
        {
            var parts = v.Split(':');
            return new
            {
                destination = parts.Length > 1 ? parts[1] : parts[0],
                type = "bind",
                source = parts[0],
                options = new[] { "rbind", "rw" }
            };
        }).ToList<object>();

        var spec = new
        {
            ociVersion = OciSpecVersion,
            process = new
            {
                terminal = false,
                user = new { uid = 0, gid = 0 },
                args,
                env,
                cwd = "/"
            },
            root = new { path = "rootfs", @readonly = false },
            mounts = bindMounts,
            linux = new
            {
                namespaces = new[]
                {
                    new { type = "pid" },
                    new { type = "network" },
                    new { type = "ipc" },
                    new { type = "uts" },
                    new { type = "mount" }
                }
            }
        };

        return JsonSerializer.Serialize(spec);
    }

    private Metadata NamespaceHeader() =>
        new() { { "containerd-namespace", options.Namespace } };
}

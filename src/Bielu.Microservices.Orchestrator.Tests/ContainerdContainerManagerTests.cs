using System.Text.Json;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Containerd;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Containerd.Services.Containers.V1;
using Containerd.Services.Snapshots.V1;
using Containerd.Services.Tasks.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using ContainerdMount = Containerd.Types.Mount;
using CreateContainerRequest = Bielu.Microservices.Orchestrator.Models.CreateContainerRequest;
using GrpcCreateContainerRequest = Containerd.Services.Containers.V1.CreateContainerRequest;
using TaskProcess = Containerd.V1.Types.Process;
using TaskStatus = Containerd.V1.Types.Status;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="ContainerdContainerManager"/>, run against <see cref="FakeContainerd"/>
/// through the generated gRPC clients.
/// </summary>
public class ContainerdContainerManagerTests
{
    private const string Ns = "apps";
    private const string C = FakeContainerd.Containers;
    private const string T = FakeContainerd.Tasks;
    private const string S = FakeContainerd.Snapshots;

    private readonly FakeContainerd _grpc = new();
    private readonly INetworkManager _networks = Substitute.For<INetworkManager>();
    private readonly OrchestratorOptions _orchestratorOptions = new() { UseDefaultNetwork = false };

    private ContainerdContainerManager Manager() => new(
        new Containers.ContainersClient(_grpc),
        new Tasks.TasksClient(_grpc),
        new Snapshots.SnapshotsClient(_grpc),
        _networks,
        new ContainerdOptions { Namespace = Ns },
        _orchestratorOptions,
        NullLogger<ContainerdContainerManager>.Instance);

    private static Container Container(string id, string? specJson = null, string snapshotKey = "", params (string Key, string Value)[] labels)
    {
        var container = new Container
        {
            Id = id,
            Image = "docker.io/library/nginx:1.25",
            SnapshotKey = snapshotKey,
            CreatedAt = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero))
        };
        if (specJson != null)
        {
            container.Spec = new Any { Value = ByteString.CopyFromUtf8(specJson) };
        }

        foreach (var (key, value) in labels)
        {
            container.Labels[key] = value;
        }

        return container;
    }

    private void RespondToCreate() =>
        _grpc.On<GrpcCreateContainerRequest, CreateContainerResponse>(C, "Create", r => new CreateContainerResponse { Container = r.Container });

    private Container CreatedContainer(int index = 0) => _grpc.Requests<GrpcCreateContainerRequest>(C, "Create")[index].Container;

    private static JsonElement Spec(Container container) => JsonDocument.Parse(container.Spec.Value.ToStringUtf8()).RootElement;

    // -----------------------------------------------------------------------
    // List / Get
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_ManagedOnly_FiltersByLabelAndMapsTaskStatuses()
    {
        // Arrange
        var response = new ListContainersResponse();
        response.Containers.Add(Container("web", labels: ("tier", "front")));
        response.Containers.Add(Container("idle"));
        _grpc.On<ListContainersRequest, ListContainersResponse>(C, "List", _ => response);
        var tasks = new ListTasksResponse();
        tasks.Tasks.Add(new TaskProcess { ContainerId = "web", Status = TaskStatus.Running });
        _grpc.On<ListTasksRequest, ListTasksResponse>(T, "List", _ => tasks);

        // Act
        var containers = await Manager().ListAsync();

        // Assert
        _grpc.Requests<ListContainersRequest>(C, "List").Single().Filters
            .ShouldBe([$"labels.\"{OrchestratorLabels.ManagedBy}\"=={OrchestratorLabels.ManagedByValue}"]);
        var web = containers.Single(c => c.Id == "web");
        web.Name.ShouldBe("web");
        web.Image.ShouldBe("docker.io/library/nginx:1.25");
        web.State.ShouldBe(ContainerState.Running);
        web.Labels["tier"].ShouldBe("front");
        web.CreatedAt.ShouldBe(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        containers.Single(c => c.Id == "idle").State.ShouldBe(ContainerState.Unknown);
        _grpc.Calls.ShouldAllBe(call => call.Namespace == Ns);
    }

    [Fact]
    public async Task ListAsync_AllContainersAndTaskListFails_ReportsUnknownStates()
    {
        // Arrange
        _orchestratorOptions.ManagedContainersOnly = false;
        var response = new ListContainersResponse();
        response.Containers.Add(new Container { Id = "web" });
        _grpc.On<ListContainersRequest, ListContainersResponse>(C, "List", _ => response)
            .Fail(T, "List", StatusCode.Unavailable);

        // Act
        var containers = await Manager().ListAsync(all: true);

        // Assert
        _grpc.Requests<ListContainersRequest>(C, "List").Single().Filters.ShouldBeEmpty();
        var web = containers.ShouldHaveSingleItem();
        web.State.ShouldBe(ContainerState.Unknown);
        web.CreatedAt.ShouldBe(DateTimeOffset.MinValue);
        web.Volumes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(TaskStatus.Created, ContainerState.Created)]
    [InlineData(TaskStatus.Running, ContainerState.Running)]
    [InlineData(TaskStatus.Stopped, ContainerState.Exited)]
    [InlineData(TaskStatus.Paused, ContainerState.Paused)]
    [InlineData(TaskStatus.Pausing, ContainerState.Unknown)]
    public async Task GetAsync_TaskStatus_MapsToContainerState(TaskStatus status, ContainerState expected)
    {
        // Arrange
        _grpc.On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse { Container = Container("web") })
            .On<GetRequest, GetResponse>(T, "Get", _ => new GetResponse { Process = new TaskProcess { Status = status } });

        // Act
        var container = await Manager().GetAsync("web");

        // Assert
        container!.State.ShouldBe(expected);
    }

    [Fact]
    public async Task GetAsync_NoTask_ReportsUnknownState()
    {
        // Arrange
        _grpc.On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse { Container = Container("web") })
            .Fail(T, "Get", StatusCode.NotFound);

        // Act
        var container = await Manager().GetAsync("web");

        // Assert
        container!.State.ShouldBe(ContainerState.Unknown);
    }

    [Fact]
    public async Task GetAsync_ContainerMissing_ReturnsNull()
    {
        // Arrange
        _grpc.Fail(C, "Get", StatusCode.NotFound);

        // Act & Assert
        (await Manager().GetAsync("missing")).ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_SpecMounts_AreMappedToBindVolumes()
    {
        // Arrange
        const string spec = """
            {"mounts":[
              {"type":"bind","source":"/srv/data","destination":"/data","options":["rbind","ro"]},
              {"type":"BIND","source":"/srv/logs","destination":"/logs","options":["rbind","rw"]},
              {"type":"bind"},
              {"type":"bind","source":"/srv/cfg","destination":"/cfg","options":"ro"},
              {"type":"proc","source":"proc","destination":"/proc"},
              {"source":"untyped","destination":"/x"}
            ]}
            """;
        _grpc.On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse { Container = Container("web", spec) })
            .Fail(T, "Get", StatusCode.NotFound);

        // Act
        var container = await Manager().GetAsync("web");

        // Assert
        container!.Volumes.Select(v => (v.HostPath, v.ContainerPath, v.ReadOnly)).ShouldBe(
        [
            ("/srv/data", "/data", true),
            ("/srv/logs", "/logs", false),
            ("", "", false),
            ("/srv/cfg", "/cfg", false),
        ]);
    }

    [Theory]
    [InlineData("{\"process\":{}}")]
    [InlineData("{\"mounts\":{}}")]
    [InlineData("not json")]
    [InlineData("   ")]
    public async Task GetAsync_SpecWithoutUsableMounts_HasNoVolumes(string spec)
    {
        // Arrange
        _grpc.On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse { Container = Container("web", spec) })
            .Fail(T, "Get", StatusCode.NotFound);

        // Act
        var container = await Manager().GetAsync("web");

        // Assert
        container!.Volumes.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Create
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_SingleReplica_SendsAContainerWithAnOciSpec()
    {
        // Arrange
        RespondToCreate();
        var request = new CreateContainerRequest
        {
            Name = "web",
            Image = "docker.io/library/nginx:1.25",
            Command = ["nginx", "-g", "daemon off;"],
            RestartPolicy = RestartPolicy.Always
        }
            .WithEnvironmentVariable("MODE", "prod")
            .WithLabel("tier", "front")
            .WithVolume("/srv/data", "/data", readOnly: true)
            .WithVolume(new VolumeMount { HostPath = "/srv/same" });

        // Act
        var id = await Manager().CreateAsync(request);

        // Assert
        id.ShouldBe("web");
        var container = CreatedContainer();
        container.Image.ShouldBe("docker.io/library/nginx:1.25");
        container.Snapshotter.ShouldBe("overlayfs");
        container.SnapshotKey.ShouldBe("web");
        container.Runtime.Name.ShouldBe("io.containerd.runc.v2");
        container.Labels[OrchestratorLabels.ManagedBy].ShouldBe(OrchestratorLabels.ManagedByValue);
        container.Labels["tier"].ShouldBe("front");
        container.Spec.TypeUrl.ShouldBe("types.containerd.io/opencontainers/runtime-spec/1/linux");

        var spec = Spec(container);
        spec.GetProperty("ociVersion").GetString().ShouldBe("1.0.2");
        spec.GetProperty("process").GetProperty("args").EnumerateArray().Select(a => a.GetString()).ShouldBe(["nginx", "-g", "daemon off;"]);
        var env = spec.GetProperty("process").GetProperty("env").EnumerateArray().Select(e => e.GetString()).ToList();
        env[0].ShouldStartWith("PATH=");
        env.ShouldContain("MODE=prod");
        var mounts = spec.GetProperty("mounts").EnumerateArray().ToList();
        mounts[0].GetProperty("destination").GetString().ShouldBe("/data");
        mounts[0].GetProperty("options")[1].GetString().ShouldBe("ro");
        mounts[1].GetProperty("destination").GetString().ShouldBe("/srv/same");
        mounts[1].GetProperty("options")[1].GetString().ShouldBe("rw");
        spec.GetProperty("linux").GetProperty("namespaces").GetArrayLength().ShouldBe(5);
    }

    [Fact]
    public async Task CreateAsync_NoCommandOrName_DefaultsToAShellAndAGeneratedId()
    {
        // Arrange
        RespondToCreate();

        // Act
        var id = await Manager().CreateAsync(new CreateContainerRequest { Image = "busybox" });

        // Assert
        id.ShouldStartWith("orchestrator-");
        Spec(CreatedContainer()).GetProperty("process").GetProperty("args").EnumerateArray().Select(a => a.GetString()).ShouldBe(["sh"]);
    }

    [Fact]
    public async Task CreateAsync_MultipleReplicas_CreatesIndexedContainersWithGroupLabels()
    {
        // Arrange
        RespondToCreate();

        // Act
        var first = await Manager().CreateAsync(new CreateContainerRequest { Name = "web", Image = "nginx", Replicas = 3 }.WithLabel("tier", "front"));

        // Assert
        first.ShouldBe("web-0");
        var created = _grpc.Requests<GrpcCreateContainerRequest>(C, "Create").Select(r => r.Container).ToList();
        created.Select(c => c.Id).ShouldBe(["web-0", "web-1", "web-2"]);
        created.ShouldAllBe(c => c.Labels[OrchestratorLabels.Group] == "web" && c.Labels["tier"] == "front");
        created.Select(c => c.Labels[OrchestratorLabels.ReplicaIndex]).ShouldBe(["0", "1", "2"]);
    }

    [Fact]
    public async Task CreateAsync_MultipleReplicasWithoutName_UsesAGeneratedGroup()
    {
        // Arrange
        RespondToCreate();

        // Act
        var first = await Manager().CreateAsync(new CreateContainerRequest { Image = "nginx", Replicas = 2 });

        // Assert
        first.ShouldStartWith("orchestrator-");
        first.ShouldEndWith("-0");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task CreateAsync_NoReplicas_Throws(int replicas)
    {
        // Act & Assert
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => Manager().CreateAsync(new CreateContainerRequest { Image = "nginx", Replicas = replicas }));
        _grpc.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ExplicitNetworks_AreStoredInTheNetworksLabel()
    {
        // Arrange
        RespondToCreate();
        var request = new CreateContainerRequest { Name = "web", Image = "nginx" };
        request.Networks.Add(new NetworkAttachment { NetworkName = "backend", Aliases = ["api"] });

        // Act
        await Manager().CreateAsync(request);

        // Assert
        var stored = JsonSerializer.Deserialize<List<NetworkAttachment>>(CreatedContainer().Labels["orchestrator.networks"])!;
        stored.ShouldHaveSingleItem().NetworkName.ShouldBe("backend");
        stored[0].Aliases.ShouldBe(["api"]);
        await _networks.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_DefaultNetworkMissing_CreatesItAndAttachesTheContainer()
    {
        // Arrange
        RespondToCreate();
        _orchestratorOptions.UseDefaultNetwork = true;
        _orchestratorOptions.DefaultNetworkName = "session-net";
        _networks.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<NetworkInfo> { new() { Id = "other", Name = "other" } }.AsReadOnly());

        // Act
        await Manager().CreateAsync(new CreateContainerRequest { Name = "web", Image = "nginx" });

        // Assert
        await _networks.Received(1).CreateAsync("session-net", Arg.Any<string>(), Arg.Any<CancellationToken>());
        var stored = JsonSerializer.Deserialize<List<NetworkAttachment>>(CreatedContainer().Labels["orchestrator.networks"])!;
        stored.ShouldHaveSingleItem().NetworkName.ShouldBe("session-net");
        stored[0].Aliases.ShouldBe(["web"]);
    }

    [Fact]
    public async Task CreateAsync_DefaultNetworkExists_DoesNotRecreateIt()
    {
        // Arrange
        RespondToCreate();
        _orchestratorOptions.UseDefaultNetwork = true;
        _orchestratorOptions.DefaultNetworkName = "session-net";
        _networks.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<NetworkInfo> { new() { Id = "session-net", Name = "x" } }.AsReadOnly());

        // Act
        await Manager().CreateAsync(new CreateContainerRequest { Name = "web", Image = "nginx" });

        // Assert
        await _networks.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_DefaultNetworkDisabled_StoresTheEmptyRequestNetworks()
    {
        // Arrange
        RespondToCreate();
        _orchestratorOptions.UseDefaultNetwork = true;
        _orchestratorOptions.DefaultNetworkName = string.Empty;

        // Act
        await Manager().CreateAsync(new CreateContainerRequest { Name = "web", Image = "nginx" });

        // Assert
        CreatedContainer().Labels["orchestrator.networks"].ShouldBe("[]");
        await _networks.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Start
    // -----------------------------------------------------------------------

    private void RespondToTaskLifecycle() =>
        _grpc.On<CreateTaskRequest, CreateTaskResponse>(T, "Create", _ => new CreateTaskResponse { Pid = 42 })
            .On<StartRequest, StartResponse>(T, "Start", _ => new StartResponse { Pid = 42 });

    [Fact]
    public async Task StartAsync_CreatesTheTaskOnTheSnapshotMountsAndConnectsNetworks()
    {
        // Arrange
        var networksLabel = JsonSerializer.Serialize(new List<NetworkAttachment>
        {
            new() { NetworkName = "backend", Aliases = ["api"] },
            new() { NetworkName = "frontend" },
        });
        var container = Container("web", snapshotKey: "web", labels: ("orchestrator.networks", networksLabel));
        _grpc.On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse { Container = container });
        var mounts = new MountsResponse();
        mounts.Mounts.Add(new ContainerdMount { Type = "overlay", Source = "overlay" });
        _grpc.On<MountsRequest, MountsResponse>(S, "Mounts", _ => mounts);
        RespondToTaskLifecycle();

        // Act
        await Manager().StartAsync("web");

        // Assert
        var mountsRequest = _grpc.Requests<MountsRequest>(S, "Mounts").Single();
        mountsRequest.Key.ShouldBe("web");
        mountsRequest.Snapshotter.ShouldBe("overlayfs");
        _grpc.Requests<CreateTaskRequest>(T, "Create").Single().Rootfs.ShouldHaveSingleItem().Type.ShouldBe("overlay");
        _grpc.Requests<StartRequest>(T, "Start").Single().ContainerId.ShouldBe("web");
        await _networks.Received(1).ConnectAsync("backend", "web", Arg.Is<IEnumerable<string>>(a => a.SequenceEqual(new[] { "api" })), Arg.Any<CancellationToken>());
        await _networks.Received(1).ConnectAsync("frontend", "web", Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
        _grpc.Calls.ShouldAllBe(call => call.Namespace == Ns);
    }

    [Fact]
    public async Task StartAsync_ExplicitSnapshotter_IsUsedForMounts()
    {
        // Arrange
        var container = Container("web", snapshotKey: "web");
        container.Snapshotter = "native";
        _grpc.On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse { Container = container })
            .On<MountsRequest, MountsResponse>(S, "Mounts", _ => new MountsResponse());
        RespondToTaskLifecycle();

        // Act
        await Manager().StartAsync("web");

        // Assert
        _grpc.Requests<MountsRequest>(S, "Mounts").Single().Snapshotter.ShouldBe("native");
    }

    [Fact]
    public async Task StartAsync_NoSnapshotKey_CreatesTheTaskWithoutRootfs()
    {
        // Arrange
        _grpc.On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse { Container = Container("web") });
        RespondToTaskLifecycle();

        // Act
        await Manager().StartAsync("web");

        // Assert
        _grpc.Requests<MountsRequest>(S, "Mounts").ShouldBeEmpty();
        _grpc.Requests<CreateTaskRequest>(T, "Create").Single().Rootfs.ShouldBeEmpty();
        await _networks.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_MountsLookupFails_StillStartsWithoutRootfs()
    {
        // Arrange
        _grpc.On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse { Container = Container("web", snapshotKey: "web") })
            .Fail(S, "Mounts", StatusCode.NotFound);
        RespondToTaskLifecycle();

        // Act
        await Manager().StartAsync("web");

        // Assert
        _grpc.Requests<CreateTaskRequest>(T, "Create").Single().Rootfs.ShouldBeEmpty();
        _grpc.Requests<StartRequest>(T, "Start").ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task StartAsync_UnusableNetworksLabel_StartsWithoutConnecting(string label)
    {
        // Arrange
        _grpc.On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse
        {
            Container = Container("web", labels: ("orchestrator.networks", label))
        });
        RespondToTaskLifecycle();

        // Act
        await Manager().StartAsync("web");

        // Assert
        _grpc.Requests<StartRequest>(T, "Start").ShouldHaveSingleItem();
        await _networks.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_NetworkConnectFails_DoesNotFailTheStart()
    {
        // Arrange
        var label = JsonSerializer.Serialize(new List<NetworkAttachment> { new() { NetworkName = "backend" } });
        _grpc.On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse
        {
            Container = Container("web", labels: ("orchestrator.networks", label))
        });
        RespondToTaskLifecycle();
        _networks.ConnectAsync(default!, default!, (IEnumerable<string>?)default, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("no task pid")));

        // Act & Assert
        await Should.NotThrowAsync(() => Manager().StartAsync("web"));
    }

    // -----------------------------------------------------------------------
    // Stop
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StopAsync_NoTimeout_SendsSigtermToAllProcessesOnly()
    {
        // Arrange
        _grpc.On<KillRequest, Empty>(T, "Kill", _ => new Empty());

        // Act
        await Manager().StopAsync("web");

        // Assert
        var kill = _grpc.Requests<KillRequest>(T, "Kill").ShouldHaveSingleItem();
        kill.Signal.ShouldBe(15u);
        kill.All.ShouldBeTrue();
        _grpc.Requests<WaitRequest>(T, "Wait").ShouldBeEmpty();
    }

    [Fact]
    public async Task StopAsync_TaskExitsWithinTimeout_DoesNotEscalate()
    {
        // Arrange
        _grpc.On<KillRequest, Empty>(T, "Kill", _ => new Empty())
            .On<WaitRequest, WaitResponse>(T, "Wait", _ => new WaitResponse { ExitStatus = 0 });

        // Act
        await Manager().StopAsync("web", TimeSpan.FromSeconds(30));

        // Assert
        _grpc.Requests<KillRequest>(T, "Kill").Select(k => k.Signal).ShouldBe([15u]);
    }

    [Fact]
    public async Task StopAsync_TimeoutExpires_EscalatesToSigkill()
    {
        // Arrange: the task ignores SIGTERM, so Wait only ends when the timeout cancels it.
        _grpc.On<KillRequest, Empty>(T, "Kill", _ => new Empty())
            .OnAsync<WaitRequest, WaitResponse>(T, "Wait", async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new WaitResponse();
            });

        // Act
        await Manager().StopAsync("web", TimeSpan.FromMilliseconds(50));

        // Assert
        _grpc.Requests<KillRequest>(T, "Kill").Select(k => k.Signal).ShouldBe([15u, 9u]);
    }

    [Fact]
    public async Task StopAsync_CallerCancels_PropagatesInsteadOfEscalating()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _grpc.On<KillRequest, Empty>(T, "Kill", _ => new Empty())
            .OnAsync<WaitRequest, WaitResponse>(T, "Wait", async (_, token) =>
            {
                await cts.CancelAsync();
                await Task.Delay(Timeout.Infinite, token);
                return new WaitResponse();
            });

        // Act & Assert
        var ex = await Should.ThrowAsync<RpcException>(() => Manager().StopAsync("web", TimeSpan.FromMinutes(5), cts.Token));
        ex.StatusCode.ShouldBe(StatusCode.Cancelled);
        _grpc.Requests<KillRequest>(T, "Kill").Select(k => k.Signal).ShouldBe([15u]);
    }

    [Fact]
    public async Task StopAsync_NoRunningTask_IsIgnored()
    {
        // Arrange
        _grpc.Fail(T, "Kill", StatusCode.NotFound);

        // Act & Assert
        await Should.NotThrowAsync(() => Manager().StopAsync("web"));
    }

    // -----------------------------------------------------------------------
    // Remove
    // -----------------------------------------------------------------------

    private void RespondToRemove(Container container) =>
        _grpc.On<KillRequest, Empty>(T, "Kill", _ => new Empty())
            .On<DeleteTaskRequest, DeleteResponse>(T, "Delete", _ => new DeleteResponse())
            .On<GetContainerRequest, GetContainerResponse>(C, "Get", _ => new GetContainerResponse { Container = container })
            .On<RemoveSnapshotRequest, Empty>(S, "Remove", _ => new Empty())
            .On<DeleteContainerRequest, Empty>(C, "Delete", _ => new Empty());

    [Fact]
    public async Task RemoveAsync_KillsTheTaskRemovesTheSnapshotAndDeletesTheContainer()
    {
        // Arrange
        RespondToRemove(Container("web", snapshotKey: "web-snap"));

        // Act
        await Manager().RemoveAsync("web");

        // Assert
        _grpc.Requests<KillRequest>(T, "Kill").Single().Signal.ShouldBe(9u);
        _grpc.Requests<DeleteTaskRequest>(T, "Delete").Single().ContainerId.ShouldBe("web");
        _grpc.Requests<RemoveSnapshotRequest>(S, "Remove").Single().Key.ShouldBe("web-snap");
        _grpc.Requests<DeleteContainerRequest>(C, "Delete").Single().Id.ShouldBe("web");
        _grpc.Calls.ShouldAllBe(call => call.Namespace == Ns);
    }

    [Fact]
    public async Task RemoveAsync_NoTaskOrSnapshot_StillDeletesTheContainer()
    {
        // Arrange
        RespondToRemove(Container("web"));
        _grpc.Fail(T, "Kill", StatusCode.NotFound);

        // Act
        await Manager().RemoveAsync("web");

        // Assert
        _grpc.Requests<RemoveSnapshotRequest>(S, "Remove").ShouldBeEmpty();
        _grpc.Requests<DeleteContainerRequest>(C, "Delete").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RemoveAsync_SnapshotAlreadyGone_StillDeletesTheContainer()
    {
        // Arrange
        RespondToRemove(Container("web", snapshotKey: "web"));
        _grpc.Fail(S, "Remove", StatusCode.NotFound);

        // Act
        await Manager().RemoveAsync("web");

        // Assert
        _grpc.Requests<DeleteContainerRequest>(C, "Delete").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RemoveAsync_RemoveVolumes_DeletesExistingBindMountDirectories()
    {
        // Arrange
        var bound = Directory.CreateTempSubdirectory("bielu-containerd-bind-").FullName;
        var missing = Path.Combine(bound + "-missing");
        var spec = JsonSerializer.Serialize(new
        {
            mounts = new object[]
            {
                new { type = "bind", source = bound, destination = "/data" },
                new { type = "bind", source = missing, destination = "/gone" },
                new { type = "bind", source = "named-volume", destination = "/named" },
            }
        });
        RespondToRemove(Container("web", spec));
        _grpc.Fail(T, "Get", StatusCode.NotFound);

        // Act
        await Manager().RemoveAsync("web", removeVolumes: true);

        // Assert
        Directory.Exists(bound).ShouldBeFalse();
        _grpc.Requests<DeleteContainerRequest>(C, "Delete").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RemoveAsync_RemoveVolumesForAMissingContainer_FailsOnDelete()
    {
        // Arrange
        _grpc.Fail(C, "Get", StatusCode.NotFound)
            .Fail(T, "Kill", StatusCode.NotFound)
            .Fail(C, "Delete", StatusCode.NotFound);

        // Act & Assert
        var ex = await Should.ThrowAsync<RpcException>(() => Manager().RemoveAsync("web", removeVolumes: true));
        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    // -----------------------------------------------------------------------
    // Unsupported operations and orchestrator
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LogsAndScale_AreNotSupported()
    {
        // Arrange
        var manager = Manager();

        // Act & Assert
        await Should.ThrowAsync<NotSupportedException>(() => manager.GetLogsAsync("web"));
        await Should.ThrowAsync<NotSupportedException>(() => manager.ScaleAsync("web", 2));
        manager.ProviderName.ShouldBe("containerd");
        manager.HostAddress.ShouldBe("172.17.0.1");
    }

    [Fact]
    public async Task Orchestrator_IsAvailableOnlyWhenListingSucceeds()
    {
        // Arrange
        var containers = Substitute.For<IContainerManager>();
        var orchestrator = new ContainerdContainerOrchestrator(
            containers, Substitute.For<IImageManager>(), _networks, Substitute.For<IVolumeManager>(),
            NullLogger<ContainerdContainerOrchestrator>.Instance);

        // Act
        var available = await orchestrator.IsAvailableAsync();
        containers.ListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<ContainerInfo>>(new RpcException(new Status(StatusCode.Unavailable, "socket"))));
        var unavailable = await orchestrator.IsAvailableAsync();

        // Assert
        available.ShouldBeTrue();
        unavailable.ShouldBeFalse();
        orchestrator.ProviderName.ShouldBe("Containerd");
        orchestrator.Networks.ShouldBeSameAs(_networks);
    }
}

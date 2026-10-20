using System.Net;
using System.Text.Json;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Docker;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Docker.DotNet;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="DockerContainerManager"/>, run against <see cref="FakeDockerApi"/> through a
/// real <see cref="DockerClient"/>. The image, network and volume managers it coordinates with are
/// substitutes.
/// </summary>
public sealed class DockerContainerManagerTests : IDisposable
{
    private readonly FakeDockerApi _api = new();
    private readonly DockerClient _client;
    private readonly IImageManager _images = Substitute.For<IImageManager>();
    private readonly INetworkManager _networks = Substitute.For<INetworkManager>();
    private readonly IVolumeManager _volumes = Substitute.For<IVolumeManager>();
    private readonly OrchestratorOptions _orchestratorOptions = new() { UseDefaultNetwork = false };

    public DockerContainerManagerTests()
    {
        _client = _api.CreateClient();
        _images.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageInfo>
        {
            new() { Id = "sha256:nginx", Tags = ["nginx:1.25"] }
        }.AsReadOnly());
        _networks.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<NetworkInfo>().AsReadOnly());
    }

    public void Dispose() => _client.Dispose();

    private DockerContainerManager Manager() =>
        new(_client, _orchestratorOptions, _images, _networks, _volumes, NullLogger<DockerContainerManager>.Instance);

    private void RespondToCreate(string id = "c1") =>
        _api.On(HttpMethod.Post, "/containers/create", new { Id = id, Warnings = Array.Empty<string>() });

    private JsonElement CreateBody(int index = 0) => _api.To(HttpMethod.Post, "/containers/create")[index].Json;

    private void Networks(params NetworkInfo[] networks) =>
        _networks.ListAsync(Arg.Any<CancellationToken>()).Returns(networks.ToList().AsReadOnly());

    // -----------------------------------------------------------------------
    // List / Get
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_ManagedOnly_FiltersByLabelAndMapsContainers()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/containers/json", """
            [{"Id":"c1","Names":["/web"],"Image":"nginx:1.25","State":"running","Created":1767225600,
              "Labels":{"tier":"front"},
              "Ports":[{"PrivatePort":80,"PublicPort":8080,"Type":"tcp","IP":"127.0.0.1"},{"PrivatePort":443,"Type":"tcp"}],
              "Mounts":[{"Source":"/srv/data","Destination":"/data","RW":false},{"Name":"vol1","Source":"","Destination":"/vol","RW":true}]},
             {"Id":"c2","Image":"busybox","State":"exited","Created":0}]
            """);

        // Act
        var containers = await Manager().ListAsync();

        // Assert
        _api.Requests.Single().Query.ShouldContain($"{OrchestratorLabels.ManagedBy}={OrchestratorLabels.ManagedByValue}");
        var web = containers[0];
        web.Id.ShouldBe("c1");
        web.Name.ShouldBe("web");
        web.State.ShouldBe(ContainerState.Running);
        web.CreatedAt.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        web.Labels["tier"].ShouldBe("front");
        web.Ports.Select(p => (p.ContainerPort, p.HostPort, p.HostIp)).ShouldBe([(80, 8080, "127.0.0.1"), (443, 0, "0.0.0.0")]);
        web.Volumes.Select(v => (v.HostPath, v.ContainerPath, v.ReadOnly)).ShouldBe([("/srv/data", "/data", true), ("vol1", "/vol", false)]);
        var bare = containers[1];
        bare.Name.ShouldBeEmpty();
        bare.State.ShouldBe(ContainerState.Exited);
        bare.Labels.ShouldBeEmpty();
        bare.Ports.ShouldBeEmpty();
        bare.Volumes.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAsync_AllContainers_SendsNoLabelFilter()
    {
        // Arrange
        _orchestratorOptions.ManagedContainersOnly = false;
        _api.On(HttpMethod.Get, "/containers/json", "[]");

        // Act
        await Manager().ListAsync(all: true);

        // Assert
        var query = _api.Requests.Single().Query;
        query.ShouldContain("all=");
        query.ShouldNotContain("filters");
    }

    [Theory]
    [InlineData("created", ContainerState.Created)]
    [InlineData("running", ContainerState.Running)]
    [InlineData("paused", ContainerState.Paused)]
    [InlineData("restarting", ContainerState.Restarting)]
    [InlineData("removing", ContainerState.Removing)]
    [InlineData("exited", ContainerState.Exited)]
    [InlineData("dead", ContainerState.Dead)]
    [InlineData("weird", ContainerState.Unknown)]
    public async Task GetAsync_ContainerStatus_MapsToContainerState(string status, ContainerState expected)
    {
        // Arrange
        _api.On(HttpMethod.Get, "/containers/c1/json", $$$"""{"Id":"c1","Name":"/web","Created":"2026-01-01T00:00:00Z","State":{"Status":"{{{status}}}"}}""");

        // Act
        var container = await Manager().GetAsync("c1");

        // Assert
        container!.State.ShouldBe(expected);
    }

    [Fact]
    public async Task GetAsync_InspectResponse_MapsConfigPortsAndMounts()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/containers/c1/json", """
            {"Id":"c1","Name":"/web","Created":"2026-01-01T00:00:00Z",
             "Config":{"Image":"nginx:1.25","Labels":{"tier":"front"},"Env":["A=1","B=x=y","=skipped","NOEQUALS"]},
             "State":{"Status":"running"},
             "NetworkSettings":{"Ports":{
               "80/tcp":[{"HostIp":"","HostPort":"32768"},{"HostIp":"::","HostPort":"not-a-port"}],
               "53/udp":[{"HostIp":"127.0.0.1","HostPort":"5353"}],
               "9000":[{"HostPort":"9000"}],
               "bad/tcp":[{"HostPort":"1"}],
               "443/tcp":null}},
             "Mounts":[{"Name":"vol1","Source":"","Destination":"/vol","RW":false}]}
            """);

        // Act
        var container = await Manager().GetAsync("c1");

        // Assert
        container!.Name.ShouldBe("web");
        container.Image.ShouldBe("nginx:1.25");
        container.Labels["tier"].ShouldBe("front");
        container.EnvironmentVariables.ShouldBe(new Dictionary<string, string> { ["A"] = "1", ["B"] = "x=y" });
        container.Ports.Select(p => (p.ContainerPort, p.HostPort, p.Protocol, p.HostIp)).ShouldBe(
            [(80, 32768, "tcp", "0.0.0.0"), (53, 5353, "udp", "127.0.0.1"), (9000, 9000, "tcp", "0.0.0.0")],
            ignoreOrder: true);
        container.Volumes.ShouldHaveSingleItem().ReadOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task GetAsync_MinimalInspectResponse_UsesDefaults()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/containers/c1/json", """{"Id":"c1","Name":"web","Created":"2026-01-01T00:00:00Z"}""");

        // Act
        var container = await Manager().GetAsync("c1");

        // Assert
        container!.Image.ShouldBeEmpty();
        container.State.ShouldBe(ContainerState.Unknown);
        container.Labels.ShouldBeEmpty();
        container.EnvironmentVariables.ShouldBeEmpty();
        container.Ports.ShouldBeEmpty();
        container.Volumes.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_ContainerMissing_ReturnsNull()
    {
        // Act & Assert
        (await Manager().GetAsync("missing")).ShouldBeNull();
    }

    // -----------------------------------------------------------------------
    // Create
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_SingleReplica_SendsTheMappedCreateParameters()
    {
        // Arrange
        RespondToCreate();
        var request = new CreateContainerRequest
        {
            Name = "web",
            Image = "nginx:1.25",
            Command = ["nginx", "-g", "daemon off;"],
            AutoRemove = true,
            RestartPolicy = RestartPolicy.OnFailure,
            MaxRestartRetries = 3
        }
            .WithEnvironmentVariable("MODE", "prod")
            .WithPort(80, 8080)
            .WithLabel("tier", "front")
            .WithVolume("/srv/data", "/data", readOnly: true);

        // Act
        var id = await Manager().CreateAsync(request);

        // Assert
        id.ShouldBe("c1");
        _api.To(HttpMethod.Post, "/containers/create").Single().Query.ShouldContain("name=web");
        var body = CreateBody();
        body.GetProperty("Image").GetString().ShouldBe("nginx:1.25");
        body.GetProperty("Env")[0].GetString().ShouldBe("MODE=prod");
        body.GetProperty("Cmd").GetArrayLength().ShouldBe(3);
        var labels = body.GetProperty("Labels");
        labels.GetProperty("tier").GetString().ShouldBe("front");
        labels.GetProperty(OrchestratorLabels.ManagedBy).GetString().ShouldBe(OrchestratorLabels.ManagedByValue);
        Guid.TryParse(labels.GetProperty(OrchestratorLabels.ManagedById).GetString(), out _).ShouldBeTrue();
        var host = body.GetProperty("HostConfig");
        host.GetProperty("PortBindings").GetProperty("80/tcp")[0].GetProperty("HostPort").GetString().ShouldBe("8080");
        host.GetProperty("Binds")[0].GetString().ShouldBe("/srv/data:/data:ro");
        host.GetProperty("AutoRemove").GetBoolean().ShouldBeTrue();
        host.GetProperty("RestartPolicy").GetProperty("Name").GetString().ShouldBe("on-failure");
        host.GetProperty("RestartPolicy").GetProperty("MaximumRetryCount").GetInt64().ShouldBe(3);
        await _images.DidNotReceive().PullAsync(Arg.Any<PullImageRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(RestartPolicy.Always, "always")]
    [InlineData(RestartPolicy.UnlessStopped, "unless-stopped")]
    [InlineData(RestartPolicy.No, "no")]
    public async Task CreateAsync_RestartPolicy_MapsToTheDockerPolicy(RestartPolicy policy, string expected)
    {
        // Arrange
        RespondToCreate();

        // Act
        await Manager().CreateAsync(new CreateContainerRequest { Image = "nginx:1.25", RestartPolicy = policy });

        // Assert
        CreateBody().GetProperty("HostConfig").GetProperty("RestartPolicy").GetProperty("Name").GetString().ShouldBe(expected);
        CreateBody().TryGetProperty("Cmd", out var cmd).ShouldBe(cmd.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task CreateAsync_ImageMissingLocally_PullsItFirst()
    {
        // Arrange
        RespondToCreate();

        // Act
        await Manager().CreateAsync(new CreateContainerRequest { Image = "redis:7" });

        // Assert
        await _images.Received(1).PullAsync(Arg.Is<PullImageRequest>(r => r.Image == "redis:7"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_LocalBoundVolumes_AreCreatedWhenMissing()
    {
        // Arrange
        RespondToCreate();
        _volumes.GetAsync("existing", Arg.Any<CancellationToken>()).Returns(new VolumeInfo { Name = "existing" });
        var request = new CreateContainerRequest { Image = "nginx:1.25" }
            .WithLocalBoundVolume("data", "/srv/data", "/data")
            .WithLocalBoundVolume("existing", "/srv/existing", "/existing");

        // Act
        await Manager().CreateAsync(request);

        // Assert
        await _volumes.Received(1).CreateAsync("data", "local",
            Arg.Is<IDictionary<string, string>>(o => o["device"] == "/srv/data"), Arg.Any<CancellationToken>());
        await _volumes.DidNotReceive().CreateAsync("existing", Arg.Any<string?>(), Arg.Any<IDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_MultipleReplicas_CreatesIndexedContainersWithGroupLabels()
    {
        // Arrange
        var next = 0;
        _api.On(HttpMethod.Post, "/containers/create", _ => new DockerResponse(HttpStatusCode.Created, new { Id = $"c{next++}" }));

        // Act
        var first = await Manager().CreateAsync(new CreateContainerRequest { Name = "web", Image = "nginx:1.25", Replicas = 3 });

        // Assert
        first.ShouldBe("c0");
        var creates = _api.To(HttpMethod.Post, "/containers/create");
        creates.Select(r => r.Query).ShouldBe(["?name=web-0", "?name=web-1", "?name=web-2"]);
        creates.Select(r => r.Json.GetProperty("Labels").GetProperty(OrchestratorLabels.ReplicaIndex).GetString()).ShouldBe(["0", "1", "2"]);
        creates.ShouldAllBe(r => r.Json.GetProperty("Labels").GetProperty(OrchestratorLabels.Group).GetString() == "web");
    }

    [Fact]
    public async Task CreateAsync_MultipleReplicasWithoutName_UsesAGeneratedGroup()
    {
        // Arrange
        RespondToCreate();

        // Act
        await Manager().CreateAsync(new CreateContainerRequest { Image = "nginx:1.25", Replicas = 2 });

        // Assert
        _api.To(HttpMethod.Post, "/containers/create")[0].Query.ShouldStartWith("?name=orchestrator-");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_NoReplicas_Throws(int replicas)
    {
        // Act & Assert
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => Manager().CreateAsync(new CreateContainerRequest { Image = "nginx:1.25", Replicas = replicas }));
        _api.Requests.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Networking at create time
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("host")]
    [InlineData("none")]
    [InlineData("container:other")]
    public async Task CreateAsync_SpecialNetworkMode_SetsNetworkModeOnly(string mode)
    {
        // Arrange
        RespondToCreate();
        var request = new CreateContainerRequest { Image = "nginx:1.25" };
        request.Networks.Add(new NetworkAttachment { NetworkName = mode });

        // Act
        await Manager().CreateAsync(request);

        // Assert
        var body = CreateBody();
        body.GetProperty("HostConfig").GetProperty("NetworkMode").GetString().ShouldBe(mode);
        (body.TryGetProperty("NetworkingConfig", out var config) && config.ValueKind != JsonValueKind.Null).ShouldBeFalse();
        await _networks.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NetworkAttachment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_UserDefinedNetworks_SetThePrimaryEndpointAndConnectEach()
    {
        // Arrange
        RespondToCreate();
        Networks(
            new NetworkInfo
            {
                Id = "net-backend",
                Name = "backend",
                Options = new Dictionary<string, string> { ["com.docker.network.driver.mtu"] = "1400", ["keep"] = "network" },
                Ipam = new NetworkIpamInfo { Config = [new NetworkIpamConfig { Subnet = "172.30.0.0/16" }] }
            },
            new NetworkInfo { Id = "net-front", Name = "frontend" });
        var request = new CreateContainerRequest { Name = "web", Image = "nginx:1.25" };
        request.Networks.Add(new NetworkAttachment
        {
            NetworkName = "backend",
            Aliases = ["api"],
            IPv4Address = "172.30.0.10",
            DriverOptions = new Dictionary<string, string> { ["keep"] = "caller" }
        });
        request.Networks.Add(new NetworkAttachment { NetworkName = "frontend", IPv4Address = "10.0.0.9", Gateway = "10.0.0.1" });
        request.Networks.Add(new NetworkAttachment { NetworkName = "host" });

        // Act
        await Manager().CreateAsync(request);

        // Assert
        var body = CreateBody();
        body.GetProperty("HostConfig").GetProperty("NetworkMode").GetString().ShouldBe("net-backend");
        var endpoint = body.GetProperty("NetworkingConfig").GetProperty("EndpointsConfig").GetProperty("net-backend");
        endpoint.GetProperty("Aliases")[0].GetString().ShouldBe("api");
        endpoint.GetProperty("IPAMConfig").GetProperty("IPv4Address").GetString().ShouldBe("172.30.0.10");
        endpoint.GetProperty("DriverOpts").GetProperty("keep").GetString().ShouldBe("caller");
        endpoint.GetProperty("DriverOpts").GetProperty("com.docker.network.driver.mtu").GetString().ShouldBe("1400");

        await _networks.Received(1).ConnectAsync("net-backend", "c1",
            Arg.Is<NetworkAttachment>(a => a.IPv4Address == "172.30.0.10" && a.Aliases.Single() == "api"), Arg.Any<CancellationToken>());
        // frontend has no user-configured subnet, so the static address and gateway are dropped.
        await _networks.Received(1).ConnectAsync("net-front", "c1",
            Arg.Is<NetworkAttachment>(a => a.IPv4Address == null && a.Gateway == null), Arg.Any<CancellationToken>());
        await _networks.DidNotReceive().ConnectAsync("host", Arg.Any<string>(), Arg.Any<NetworkAttachment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_PrimaryWithoutSubnets_DropsStaticAddressing()
    {
        // Arrange
        RespondToCreate();
        Networks(new NetworkInfo { Id = "net-1", Name = "app", Ipam = new NetworkIpamInfo { Config = [new NetworkIpamConfig()] } });
        var request = new CreateContainerRequest { Image = "nginx:1.25" };
        request.Networks.Add(new NetworkAttachment { NetworkName = "app", IPv6Address = "fd00::5" });

        // Act
        await Manager().CreateAsync(request);

        // Assert
        var endpoint = CreateBody().GetProperty("NetworkingConfig").GetProperty("EndpointsConfig").GetProperty("net-1");
        (endpoint.TryGetProperty("IPAMConfig", out var ipam) && ipam.ValueKind != JsonValueKind.Null).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateAsync_DefaultBridge_StripsAliases()
    {
        // Arrange
        RespondToCreate();
        Networks(new NetworkInfo { Id = "bridge-id", Name = "bridge" });
        var request = new CreateContainerRequest { Image = "nginx:1.25" };
        request.Networks.Add(new NetworkAttachment { NetworkName = "bridge", Aliases = ["web"] });

        // Act
        await Manager().CreateAsync(request);

        // Assert
        CreateBody().GetProperty("NetworkingConfig").GetProperty("EndpointsConfig").GetProperty("bridge-id")
            .GetProperty("Aliases").GetArrayLength().ShouldBe(0);
        await _networks.Received(1).ConnectAsync("bridge-id", "c1", Arg.Is<NetworkAttachment>(a => a.Aliases.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_UnknownNetwork_ThrowsBeforeCreating()
    {
        // Arrange
        var request = new CreateContainerRequest { Image = "nginx:1.25" };
        request.Networks.Add(new NetworkAttachment { NetworkName = "missing-a" });
        request.Networks.Add(new NetworkAttachment { NetworkName = "missing-b" });

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => Manager().CreateAsync(request));
        ex.Message.ShouldContain("missing-a, missing-b");
        _api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_DefaultNetworkMissing_CreatesItAndUsesTheNameAsAlias()
    {
        // Arrange
        RespondToCreate();
        _orchestratorOptions.UseDefaultNetwork = true;
        _orchestratorOptions.DefaultNetworkName = "session-net";
        _networks.ListAsync(Arg.Any<CancellationToken>()).Returns(
            new List<NetworkInfo>().AsReadOnly(),
            new List<NetworkInfo> { new() { Id = "net-s", Name = "session-net" } }.AsReadOnly());

        // Act
        await Manager().CreateAsync(new CreateContainerRequest { Name = "web", Image = "nginx:1.25" });

        // Assert
        await _networks.Received(1).CreateAsync("session-net", Arg.Any<string>(), Arg.Any<CancellationToken>());
        CreateBody().GetProperty("NetworkingConfig").GetProperty("EndpointsConfig").GetProperty("net-s")
            .GetProperty("Aliases")[0].GetString().ShouldBe("web");
    }

    [Fact]
    public async Task CreateAsync_DefaultNetworkExistsAndNoName_AttachesWithoutAliases()
    {
        // Arrange
        RespondToCreate();
        _orchestratorOptions.UseDefaultNetwork = true;
        _orchestratorOptions.DefaultNetworkName = "session-net";
        Networks(new NetworkInfo { Id = "session-net", Name = "something-else" });

        // Act
        await Manager().CreateAsync(new CreateContainerRequest { Image = "nginx:1.25" });

        // Assert
        await _networks.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _networks.Received(1).ConnectAsync("session-net", "c1", Arg.Is<NetworkAttachment>(a => a.Aliases.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_DefaultNetworkDisabledByEmptyName_CreatesWithoutNetworks()
    {
        // Arrange
        RespondToCreate();
        _orchestratorOptions.UseDefaultNetwork = true;
        _orchestratorOptions.DefaultNetworkName = string.Empty;

        // Act
        await Manager().CreateAsync(new CreateContainerRequest { Image = "nginx:1.25" });

        // Assert
        await _networks.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _networks.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NetworkAttachment>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAndStop_CallTheContainerEndpoints()
    {
        // Arrange
        _api.On(HttpMethod.Post, "/containers/c1/start", null, HttpStatusCode.NoContent)
            .On(HttpMethod.Post, "/containers/c1/stop", null, HttpStatusCode.NoContent);

        // Act
        await Manager().StartAsync("c1");
        await Manager().StopAsync("c1", TimeSpan.FromSeconds(12));
        await Manager().StopAsync("c1");

        // Assert
        var stops = _api.To(HttpMethod.Post, "/containers/c1/stop");
        stops[0].Query.ShouldContain("t=12");
        stops[1].Query.ShouldNotContain("t=");
        _api.To(HttpMethod.Post, "/containers/c1/start").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RemoveAsync_SendsForceAndVolumeFlags()
    {
        // Arrange
        _api.On(HttpMethod.Delete, "/containers/c1", null, HttpStatusCode.NoContent);

        // Act
        await Manager().RemoveAsync("c1", force: true);

        // Assert
        var remove = _api.Requests.ShouldHaveSingleItem();
        // Docker.DotNet sends true as 1 and leaves false flags out.
        remove.Query.ShouldContain("force=1");
        remove.Query.ShouldNotContain("v=");
    }

    [Fact]
    public async Task RemoveAsync_RemoveVolumes_CleansBindMountsAndRemovesNamedVolumes()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/containers/c1/json", """
                {"Id":"c1","Name":"/web","Created":"2026-01-01T00:00:00Z",
                 "Mounts":[{"Source":"/srv/data","Destination":"/data","RW":true},
                           {"Name":"vol1","Source":"","Destination":"/vol","RW":true},
                           {"Name":"vol-gone","Source":"","Destination":"/gone","RW":true},
                           {"Name":"vol-busy","Source":"","Destination":"/busy","RW":true},
                           {"Name":"","Source":"","Destination":"/anon","RW":true}]}
                """)
            .On(HttpMethod.Delete, "/containers/c1", null, HttpStatusCode.NoContent)
            .Fail(HttpMethod.Get, "/images/alpine:latest/json", HttpStatusCode.NotFound)
            .On(HttpMethod.Post, "/containers/create", new { Id = "helper" }, HttpStatusCode.Created)
            .On(HttpMethod.Post, "/containers/helper/start", null, HttpStatusCode.NoContent)
            .On(HttpMethod.Post, "/containers/helper/wait", new { StatusCode = 1 })
            .On(HttpMethod.Delete, "/containers/helper", null, HttpStatusCode.NoContent)
            .On(HttpMethod.Delete, "/volumes/vol1", null, HttpStatusCode.NoContent)
            .Fail(HttpMethod.Delete, "/volumes/vol-gone", HttpStatusCode.NotFound)
            .Fail(HttpMethod.Delete, "/volumes/vol-busy", HttpStatusCode.Conflict);

        // Act
        await Manager().RemoveAsync("c1", force: true, removeVolumes: true);

        // Assert
        await _images.Received(1).PullAsync(Arg.Is<PullImageRequest>(r => r.Image == "alpine" && r.Tag == "latest"), Arg.Any<CancellationToken>());
        var helper = CreateBody();
        helper.GetProperty("Image").GetString().ShouldBe("alpine:latest");
        helper.GetProperty("HostConfig").GetProperty("Binds")[0].GetString().ShouldBe("/srv/data:/cleanup");
        _api.To(HttpMethod.Delete, "/containers/helper").Single().Query.ShouldContain("force=1");
        // Docker.DotNet.Enhanced 4.2.0 does not forward the force flag of Volumes.RemoveAsync.
        _api.To(HttpMethod.Delete, "/volumes/vol1").ShouldHaveSingleItem();
        _api.To(HttpMethod.Delete, "/volumes/vol-gone").ShouldHaveSingleItem();
        _api.To(HttpMethod.Delete, "/volumes/vol-busy").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RemoveAsync_BindCleanupFails_StillCompletes()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/containers/c1/json", """
                {"Id":"c1","Name":"/web","Created":"2026-01-01T00:00:00Z","Mounts":[{"Source":"/srv/data","Destination":"/data","RW":true}]}
                """)
            .On(HttpMethod.Delete, "/containers/c1", null, HttpStatusCode.NoContent)
            .On(HttpMethod.Get, "/images/alpine:latest/json", """{"Id":"sha256:alpine"}""")
            .Fail(HttpMethod.Post, "/containers/create", HttpStatusCode.InternalServerError);

        // Act & Assert
        await Should.NotThrowAsync(() => Manager().RemoveAsync("c1", removeVolumes: true));
        await _images.DidNotReceive().PullAsync(Arg.Any<PullImageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_RemoveVolumesForAMissingContainer_StillRemoves()
    {
        // Arrange
        _api.On(HttpMethod.Delete, "/containers/c1", null, HttpStatusCode.NoContent);

        // Act
        await Manager().RemoveAsync("c1", removeVolumes: true);

        // Assert
        _api.To(HttpMethod.Delete, "/containers/c1").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetLogsAsync_DemultiplexesStdout()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/containers/c1/json", """{"Id":"c1","Name":"/web","Created":"2026-01-01T00:00:00Z","Config":{"Tty":false}}""")
            .On(HttpMethod.Get, "/containers/c1/logs", _ => new DockerResponse(
                HttpStatusCode.OK,
                FakeDockerApi.StdoutFrame("line one\n").Concat(FakeDockerApi.StdoutFrame("line two\n")).ToArray(),
                "application/vnd.docker.multiplexed-stream"));

        // Act
        var logs = await Manager().GetLogsAsync("c1", stdout: true, stderr: false);

        // Assert
        logs.ShouldBe("line one\nline two\n");
        var query = _api.To(HttpMethod.Get, "/containers/c1/logs").Single().Query;
        query.ShouldContain("stdout=1");
        query.ShouldNotContain("stderr");
    }

    [Fact]
    public async Task ScaleAsync_IsNotSupported()
    {
        // Act & Assert
        await Should.ThrowAsync<NotSupportedException>(() => Manager().ScaleAsync("c1", 2));
        Manager().ProviderName.ShouldBe("Docker");
        Manager().HostAddress.ShouldBe("host.docker.internal");
    }
}

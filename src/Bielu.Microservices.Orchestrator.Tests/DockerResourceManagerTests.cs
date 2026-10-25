using System.Net;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Docker;
using Bielu.Microservices.Orchestrator.Docker.Configuration;
using Bielu.Microservices.Orchestrator.Docker.Extensions;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Podman.Extensions;
using Docker.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for the Docker image, volume and network managers, the orchestrator and the Docker and
/// Podman registrations, run against <see cref="FakeDockerApi"/> through a real <see cref="DockerClient"/>.
/// </summary>
public sealed class DockerResourceManagerTests : IDisposable
{
    private readonly FakeDockerApi _api = new();
    private readonly DockerClient _client;

    public DockerResourceManagerTests() => _client = _api.CreateClient();

    public void Dispose() => _client.Dispose();

    private DockerImageManager Images() => new(_client, NullLogger<DockerImageManager>.Instance);

    private DockerVolumeManager Volumes() => new(_client, NullLogger<DockerVolumeManager>.Instance);

    private DockerNetworkManager Networks() => new(_client, NullLogger<DockerNetworkManager>.Instance);

    // -----------------------------------------------------------------------
    // Images
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ImageListAsync_MapsImages()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/images/json", """
            [{"Id":"sha256:a","RepoTags":["nginx:1.25"],"Size":1024,"Created":1767225600,"Labels":{"maintainer":"x"}},
             {"Id":"sha256:b","Size":0,"Created":0}]
            """);

        // Act
        var images = await Images().ListAsync();

        // Assert
        _api.Requests.Single().Query.ShouldContain("all=1");
        images[0].Tags.ShouldBe(["nginx:1.25"]);
        images[0].Size.ShouldBe(1024);
        images[0].CreatedAt.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        images[0].Labels["maintainer"].ShouldBe("x");
        images[1].Tags.ShouldBeEmpty();
        images[1].Labels.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImageGetAsync_FoundOrMissing()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/images/nginx:1.25/json", """
                {"Id":"sha256:a","RepoTags":["nginx:1.25"],"Size":10,"Created":"2026-01-01T00:00:00Z","Config":{"Labels":{"k":"v"}}}
                """)
            .On(HttpMethod.Get, "/images/bare/json", """{"Id":"sha256:b","Size":1}""");

        // Act
        var found = await Images().GetAsync("nginx:1.25");
        var bare = await Images().GetAsync("bare");
        var missing = await Images().GetAsync("missing");

        // Assert
        found!.Labels["k"].ShouldBe("v");
        found.CreatedAt.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        bare!.Tags.ShouldBeEmpty();
        bare.Labels.ShouldBeEmpty();
        missing.ShouldBeNull();
    }

    [Fact]
    public async Task PullAsync_CreatesTheImageWithOrWithoutCredentials()
    {
        // Arrange
        _api.On(HttpMethod.Post, "/images/create", """{"status":"Pulling from library/nginx"}""" + "\n" + """{"status":"Done"}""");

        // Act
        await Images().PullAsync(new PullImageRequest { Image = "nginx", Tag = "1.25" });
        await Images().PullAsync(new PullImageRequest
        {
            Image = "registry.test/app",
            Tag = "2",
            Credentials = new RegistryCredentials { ServerAddress = "registry.test", Username = "u", Password = "p" }
        });

        // Assert
        var pulls = _api.To(HttpMethod.Post, "/images/create");
        pulls[0].Query.ShouldContain("fromImage=nginx");
        pulls[0].Query.ShouldContain("tag=1.25");
        pulls[1].Query.ShouldContain("fromImage=registry.test/app");
    }

    [Fact]
    public async Task ImageRemoveAndTag_CallTheImageEndpoints()
    {
        // Arrange
        _api.On(HttpMethod.Delete, "/images/sha256:a", "[]")
            .On(HttpMethod.Post, "/images/sha256:a/tag", null, HttpStatusCode.Created);

        // Act
        await Images().RemoveAsync("sha256:a", force: true);
        await Images().TagAsync("sha256:a", "registry.test/app", "v2");

        // Assert
        _api.To(HttpMethod.Delete, "/images/sha256:a").Single().Query.ShouldContain("force=1");
        var tag = _api.To(HttpMethod.Post, "/images/sha256:a/tag").Single().Query;
        tag.ShouldContain("repo=registry.test/app");
        tag.ShouldContain("tag=v2");
    }

    // -----------------------------------------------------------------------
    // Volumes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VolumeListAsync_MapsVolumes()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/volumes", """
            {"Volumes":[{"Name":"data","Driver":"local","Mountpoint":"/var/lib/docker/volumes/data/_data",
                         "Labels":{"a":"b"},"Options":{"type":"none"},"CreatedAt":"2026-02-03T04:05:06Z"},
                        {"Name":"bare","Driver":"local","Mountpoint":"","CreatedAt":"not a date"}]}
            """);

        // Act
        var volumes = await Volumes().ListAsync();

        // Assert
        volumes[0].MountPoint.ShouldEndWith("_data");
        volumes[0].Labels["a"].ShouldBe("b");
        volumes[0].DriverOptions["type"].ShouldBe("none");
        volumes[0].CreatedAt.ShouldBe(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));
        volumes[1].Labels.ShouldBeEmpty();
        volumes[1].DriverOptions.ShouldBeEmpty();
        volumes[1].CreatedAt.ShouldBe(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task VolumeListAsync_NoVolumes_ReturnsEmpty()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/volumes", """{"Volumes":null}""");

        // Act & Assert
        (await Volumes().ListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task VolumeGetAsync_FoundOrMissing()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/volumes/data", """{"Name":"data","Driver":"local","Mountpoint":"/m"}""");

        // Act & Assert
        (await Volumes().GetAsync("data"))!.Driver.ShouldBe("local");
        (await Volumes().GetAsync("missing")).ShouldBeNull();
    }

    [Fact]
    public async Task VolumeCreateAndRemove_CallTheVolumeEndpoints()
    {
        // Arrange
        _api.On(HttpMethod.Post, "/volumes/create", r => new DockerResponse(HttpStatusCode.Created, r.Body))
            .On(HttpMethod.Delete, "/volumes/data", null, HttpStatusCode.NoContent);

        // Act
        var created = await Volumes().CreateAsync("data", driverOptions: new Dictionary<string, string> { ["type"] = "none" });
        var plain = await Volumes().CreateAsync("plain", "nfs");
        await Volumes().RemoveAsync("data", force: true);

        // Assert
        created.Name.ShouldBe("data");
        created.Driver.ShouldBe("local");
        var bodies = _api.To(HttpMethod.Post, "/volumes/create");
        bodies[0].Json.GetProperty("DriverOpts").GetProperty("type").GetString().ShouldBe("none");
        plain.Driver.ShouldBe("nfs");
        _api.To(HttpMethod.Delete, "/volumes/data").ShouldHaveSingleItem();
    }

    // -----------------------------------------------------------------------
    // Networks
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NetworkListAsync_InspectsEachNetworkForItsContainers()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/networks", """[{"Id":"n1","Name":"app"},{"Id":"n2","Name":"bare"}]""")
            .On(HttpMethod.Get, "/networks/n1", """
                {"Id":"n1","Name":"app","Driver":"bridge","Scope":"local","Created":"2026-01-01T00:00:00Z",
                 "EnableIPv4":true,"EnableIPv6":true,"Internal":true,"Attachable":true,"Ingress":false,"ConfigOnly":false,
                 "IPAM":{"Driver":"default","Options":{"o":"1"},
                         "Config":[{"Subnet":"172.30.0.0/16","IPRange":"172.30.5.0/24","Gateway":"172.30.0.1","AuxiliaryAddresses":{"r":"172.30.0.2"}},{}]},
                 "Containers":{"c1":{"Name":"web","EndpointID":"e1","MacAddress":"02:42","IPv4Address":"172.30.0.3/16","IPv6Address":""},
                               "c2":{}},
                 "Options":{"mtu":"1400"},"Labels":{"tier":"front"}}
                """)
            .On(HttpMethod.Get, "/networks/n2", """{"Id":"n2","Name":"bare","Driver":"null"}""");

        // Act
        var networks = await Networks().ListAsync();

        // Assert
        var app = networks[0];
        app.Scope.ShouldBe("local");
        app.Created.ShouldBe(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        (app.EnableIPv4, app.EnableIPv6, app.Internal, app.Attachable).ShouldBe((true, true, true, true));
        app.Ipam!.Driver.ShouldBe("default");
        app.Ipam.Options["o"].ShouldBe("1");
        app.Ipam.Config[0].AuxAddress["r"].ShouldBe("172.30.0.2");
        app.Ipam.Config[1].Subnet.ShouldBeEmpty();
        app.Ipam.Config[1].AuxAddress.ShouldBeEmpty();
        app.Containers["c1"].IPv4Address.ShouldBe("172.30.0.3/16");
        app.Containers["c2"].Name.ShouldBeEmpty();
        app.Options["mtu"].ShouldBe("1400");
        app.Labels["tier"].ShouldBe("front");
        var bare = networks[1];
        bare.Scope.ShouldBeEmpty();
        bare.Created.ShouldBeNull();
        bare.Ipam.ShouldBeNull();
        bare.Options.ShouldBeEmpty();
        bare.Labels.ShouldBeEmpty();
        bare.Containers.ShouldBeEmpty();
    }

    [Fact]
    public async Task NetworkCreateRemoveAndDisconnect_CallTheNetworkEndpoints()
    {
        // Arrange
        _api.On(HttpMethod.Post, "/networks/create", new { Id = "n-new" }, HttpStatusCode.Created)
            .On(HttpMethod.Delete, "/networks/n-new", null, HttpStatusCode.NoContent)
            .On(HttpMethod.Post, "/networks/n-new/disconnect", null, HttpStatusCode.OK);

        // Act
        var id = await Networks().CreateAsync("app", "overlay");
        await Networks().DisconnectAsync("n-new", "c1");
        await Networks().RemoveAsync("n-new");

        // Assert
        id.ShouldBe("n-new");
        var create = _api.To(HttpMethod.Post, "/networks/create").Single().Json;
        create.GetProperty("Name").GetString().ShouldBe("app");
        create.GetProperty("Driver").GetString().ShouldBe("overlay");
        _api.To(HttpMethod.Post, "/networks/n-new/disconnect").Single().Json.GetProperty("Container").GetString().ShouldBe("c1");
        _api.To(HttpMethod.Delete, "/networks/n-new").ShouldHaveSingleItem();
    }

    private void ContainerOnNetworks(string networksJson) =>
        _api.On(HttpMethod.Get, "/containers/c1/json",
            $$$"""{"Id":"c1","Name":"/web","Created":"2026-01-01T00:00:00Z","NetworkSettings":{"Networks":{{{networksJson}}}}}""");

    [Fact]
    public async Task ConnectAsync_FullAttachment_SendsEveryEndpointSetting()
    {
        // Arrange
        ContainerOnNetworks("{}");
        _api.On(HttpMethod.Post, "/networks/n1/connect", null, HttpStatusCode.OK);
        var attachment = new NetworkAttachment
        {
            NetworkName = "n1",
            Aliases = ["api"],
            IPv4Address = "172.30.0.10",
            IPv6Address = "fd00::10",
            Gateway = "172.30.0.1",
            MacAddress = "02:42:ac:1e:00:0a",
            Links = ["db:database"],
            DriverOptions = new Dictionary<string, string> { ["k"] = "v" },
            DnsNames = ["api.local"]
        };

        // Act
        await Networks().ConnectAsync("n1", "c1", attachment);

        // Assert
        var body = _api.To(HttpMethod.Post, "/networks/n1/connect").Single().Json;
        body.GetProperty("Container").GetString().ShouldBe("c1");
        var endpoint = body.GetProperty("EndpointConfig");
        endpoint.GetProperty("NetworkID").GetString().ShouldBe("n1");
        endpoint.GetProperty("Aliases")[0].GetString().ShouldBe("api");
        endpoint.GetProperty("IPAMConfig").GetProperty("IPv4Address").GetString().ShouldBe("172.30.0.10");
        endpoint.GetProperty("IPAMConfig").GetProperty("IPv6Address").GetString().ShouldBe("fd00::10");
        endpoint.GetProperty("IPAddress").GetString().ShouldBe("172.30.0.10");
        endpoint.GetProperty("GlobalIPv6Address").GetString().ShouldBe("fd00::10");
        endpoint.GetProperty("Gateway").GetString().ShouldBe("172.30.0.1");
        endpoint.GetProperty("MacAddress").GetString().ShouldBe("02:42:ac:1e:00:0a");
        endpoint.GetProperty("Links")[0].GetString().ShouldBe("db:database");
        endpoint.GetProperty("DriverOpts").GetProperty("k").GetString().ShouldBe("v");
        endpoint.GetProperty("DNSNames")[0].GetString().ShouldBe("api.local");
    }

    [Fact]
    public async Task ConnectAsync_AliasOverloadAndIPv6Only_SendsTheMinimalEndpoint()
    {
        // Arrange
        ContainerOnNetworks("""{"bridge":{"NetworkID":"other"}}""");
        _api.On(HttpMethod.Post, "/networks/n1/connect", null, HttpStatusCode.OK);

        // Act
        await Networks().ConnectAsync("n1", "c1", ["web"]);
        await Networks().ConnectAsync("n1", "c1", new NetworkAttachment { NetworkName = "n1", IPv6Address = "fd00::7" });
        await Networks().ConnectAsync("n1", "c1", (NetworkAttachment)null!);

        // Assert
        var bodies = _api.To(HttpMethod.Post, "/networks/n1/connect").Select(r => r.Json.GetProperty("EndpointConfig")).ToList();
        bodies[0].GetProperty("Aliases")[0].GetString().ShouldBe("web");
        bodies[1].GetProperty("IPAMConfig").GetProperty("IPv4Address").GetString().ShouldBeEmpty();
        bodies[1].GetProperty("GlobalIPv6Address").GetString().ShouldBe("fd00::7");
        bodies[2].GetProperty("Aliases").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("""{"n1":{"NetworkID":"n1"}}""")]
    [InlineData("""{"app":{"NetworkID":"N1"}}""")]
    public async Task ConnectAsync_AlreadyConnected_SkipsTheConnectCall(string networks)
    {
        // Arrange
        ContainerOnNetworks(networks);

        // Act
        await Networks().ConnectAsync("n1", "c1", ["web"]);

        // Assert
        _api.To(HttpMethod.Post, "/networks/n1/connect").ShouldBeEmpty();
    }

    [Fact]
    public async Task ConnectAsync_Conflict_IsTreatedAsAlreadyConnected()
    {
        // Arrange
        ContainerOnNetworks("{}");
        _api.Fail(HttpMethod.Post, "/networks/n1/connect", HttpStatusCode.Conflict);

        // Act & Assert
        await Should.NotThrowAsync(() => Networks().ConnectAsync("n1", "c1"));
    }

    [Fact]
    public async Task ConnectAsync_OtherFailures_AreRethrown()
    {
        // Arrange
        ContainerOnNetworks("{}");
        _api.Fail(HttpMethod.Post, "/networks/n1/connect", HttpStatusCode.InternalServerError);

        // Act & Assert
        var ex = await Should.ThrowAsync<DockerApiException>(() => Networks().ConnectAsync("n1", "c1"));
        ex.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    // -----------------------------------------------------------------------
    // Orchestrator and registration
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Orchestrator_IsAvailableOnlyWhenListingSucceeds()
    {
        // Arrange
        var containers = Substitute.For<IContainerManager>();
        var orchestrator = new DockerContainerOrchestrator(
            containers, Images(), Networks(), Volumes(), NullLogger<DockerContainerOrchestrator>.Instance);

        // Act
        var available = await orchestrator.IsAvailableAsync();
        containers.ListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<ContainerInfo>>(new HttpRequestException("daemon down")));
        var unavailable = await orchestrator.IsAvailableAsync();

        // Assert
        available.ShouldBeTrue();
        unavailable.ShouldBeFalse();
        orchestrator.ProviderName.ShouldBe("Docker");
        orchestrator.Containers.ShouldBeSameAs(containers);
        orchestrator.Images.ShouldBeOfType<DockerImageManager>();
        orchestrator.Networks.ShouldBeOfType<DockerNetworkManager>();
        orchestrator.Volumes.ShouldBeOfType<DockerVolumeManager>();
    }

    [Fact]
    public void AddDocker_RegistersTheProviderWithTheConfiguredEndpoint()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging();

        // Act
        services.AddMicroservicesOrchestrator(b => b.AddDocker(o => o.Endpoint = "tcp://docker.test:2375"));
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<DockerOptions>().Endpoint.ShouldBe("tcp://docker.test:2375");
        provider.GetRequiredService<DockerClient>().ShouldNotBeNull();
        provider.GetRequiredService<IContainerManager>().ShouldBeOfType<DockerContainerManager>();
        provider.GetRequiredService<IContainerOrchestrator>().ShouldBeOfType<DockerContainerOrchestrator>();
    }

    [Fact]
    public void DockerOptions_Defaults_PointAtThePlatformSocket()
    {
        // Act
        var options = new DockerOptions();

        // Assert
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.Endpoint.ShouldBe(OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
    }

    [Fact]
    public void AddPodman_RegistersTheDockerCompatibleManagers()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging();

        // Act
        services.AddMicroservicesOrchestrator(b => b.AddPodman(o => o.Endpoint = "tcp://podman.test:8888"));
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<IContainerOrchestrator>().ProviderName.ShouldBe("Podman");
        provider.GetRequiredService<IContainerManager>().ShouldBeOfType<DockerContainerManager>();
    }
}

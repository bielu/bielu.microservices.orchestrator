using System.Diagnostics;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Verifies the spans and metrics every OpenTelemetry manager decorator emits, on success and on
/// failure. Each case names the span, calls the decorator and, for the failure run, makes the inner
/// manager throw.
/// </summary>
[Collection(TelemetryCollection.Name)]
public class OpenTelemetryDecoratorTelemetryTests
{
    private static readonly InvalidOperationException Failure = new("runtime exploded");

    private sealed record Case(
        string Operation,
        string CountInstrument,
        string DurationInstrument,
        Func<Task> Act,
        Action MakeInnerFail);

    private readonly IContainerManager _containers = Substitute.For<IContainerManager>();
    private readonly IImageManager _images = Substitute.For<IImageManager>();
    private readonly INetworkManager _networks = Substitute.For<INetworkManager>();
    private readonly IVolumeManager _volumes = Substitute.For<IVolumeManager>();

    private Dictionary<string, Case> Cases()
    {
        var containers = new OpenTelemetryContainerManagerDecorator(_containers);
        var images = new OpenTelemetryImageManagerDecorator(_images);
        var networks = new OpenTelemetryNetworkManagerDecorator(_networks);
        var volumes = new OpenTelemetryVolumeManagerDecorator(_volumes);

        const string c = "orchestrator.container.operations", cd = "orchestrator.container.operation.duration";
        const string i = "orchestrator.image.operations", id = "orchestrator.image.operation.duration";
        const string n = "orchestrator.network.operations", nd = "orchestrator.network.operation.duration";
        const string v = "orchestrator.volume.operations", vd = "orchestrator.volume.operation.duration";

        Case[] cases =
        [
            new("container.list", c, cd, () => containers.ListAsync(all: true),
                () => _containers.ListAsync(default, default).ThrowsAsyncForAnyArgs(Failure)),
            new("container.get", c, cd, () => containers.GetAsync("ctr-1"),
                () => _containers.GetAsync(default!, default).ThrowsAsyncForAnyArgs(Failure)),
            new("container.create", c, cd, () => containers.CreateAsync(new CreateContainerRequest { Name = "web", Image = "nginx:1.25" }),
                () => _containers.CreateAsync(default!, default).ThrowsAsyncForAnyArgs(Failure)),
            new("container.start", c, cd, () => containers.StartAsync("ctr-1"),
                () => _containers.StartAsync(default!, default).ThrowsAsyncForAnyArgs(Failure)),
            new("container.stop", c, cd, () => containers.StopAsync("ctr-1", TimeSpan.FromSeconds(5)),
                () => _containers.StopAsync(default!, default, default).ThrowsAsyncForAnyArgs(Failure)),
            new("container.remove", c, cd, () => containers.RemoveAsync("ctr-1", force: true, removeVolumes: true),
                () => _containers.RemoveAsync(default!, default, default, default).ThrowsAsyncForAnyArgs(Failure)),
            new("container.get_logs", c, cd, () => containers.GetLogsAsync("ctr-1", stdout: true, stderr: false),
                () => _containers.GetLogsAsync(default!, default, default, default).ThrowsAsyncForAnyArgs(Failure)),
            new("container.scale", c, cd, () => containers.ScaleAsync("ctr-1", 3),
                () => _containers.ScaleAsync(default!, default, default).ThrowsAsyncForAnyArgs(Failure)),

            new("image.list", i, id, () => images.ListAsync(),
                () => _images.ListAsync(default).ThrowsAsyncForAnyArgs(Failure)),
            new("image.get", i, id, () => images.GetAsync("sha256:abc"),
                () => _images.GetAsync(default!, default).ThrowsAsyncForAnyArgs(Failure)),
            new("image.pull", i, id, () => images.PullAsync(new PullImageRequest { Image = "nginx", Tag = "1.25" }),
                () => _images.PullAsync(default!, default).ThrowsAsyncForAnyArgs(Failure)),
            new("image.remove", i, id, () => images.RemoveAsync("sha256:abc", force: true),
                () => _images.RemoveAsync(default!, default, default).ThrowsAsyncForAnyArgs(Failure)),
            new("image.tag", i, id, () => images.TagAsync("sha256:abc", "registry/app", "v2"),
                () => _images.TagAsync(default!, default!, default!, default).ThrowsAsyncForAnyArgs(Failure)),

            new("network.list", n, nd, () => networks.ListAsync(),
                () => _networks.ListAsync(default).ThrowsAsyncForAnyArgs(Failure)),
            new("network.create", n, nd, () => networks.CreateAsync("app-net", "overlay"),
                () => _networks.CreateAsync(default!, default!, default).ThrowsAsyncForAnyArgs(Failure)),
            new("network.remove", n, nd, () => networks.RemoveAsync("net-1"),
                () => _networks.RemoveAsync(default!, default).ThrowsAsyncForAnyArgs(Failure)),
            new("network.connect", n, nd, () => networks.ConnectAsync("net-1", "ctr-1", ["web", "www"]),
                () => _networks.ConnectAsync(default!, default!, (IEnumerable<string>?)default, default).ThrowsAsyncForAnyArgs(Failure)),
            new("network.disconnect", n, nd, () => networks.DisconnectAsync("net-1", "ctr-1"),
                () => _networks.DisconnectAsync(default!, default!, default).ThrowsAsyncForAnyArgs(Failure)),

            new("volume.list", v, vd, () => volumes.ListAsync(),
                () => _volumes.ListAsync(default).ThrowsAsyncForAnyArgs(Failure)),
            new("volume.get", v, vd, () => volumes.GetAsync("data"),
                () => _volumes.GetAsync(default!, default).ThrowsAsyncForAnyArgs(Failure)),
            new("volume.create", v, vd, () => volumes.CreateAsync("data", "local"),
                () => _volumes.CreateAsync(default!, default, default, default).ThrowsAsyncForAnyArgs(Failure)),
            new("volume.remove", v, vd, () => volumes.RemoveAsync("data", force: true),
                () => _volumes.RemoveAsync(default!, default, default).ThrowsAsyncForAnyArgs(Failure)),
        ];

        return cases.ToDictionary(x => x.Operation);
    }

    public static TheoryData<string> Operations()
    {
        var data = new TheoryData<string>();
        foreach (var operation in new OpenTelemetryDecoratorTelemetryTests().Cases().Keys)
        {
            data.Add(operation);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Operation_Succeeds_RecordsASpanAndSuccessMetrics(string operation)
    {
        // Arrange
        var @case = Cases()[operation];
        using var capture = new TelemetryCapture();

        // Act
        await @case.Act();

        // Assert
        capture.Single(operation).Status.ShouldBe(ActivityStatusCode.Unset);
        capture.For(@case.CountInstrument, operation).ShouldHaveSingleItem().Tags["status"].ShouldBe("success");
        capture.For(@case.DurationInstrument, operation).ShouldHaveSingleItem().Value.ShouldBeGreaterThanOrEqualTo(0);
        capture.For("orchestrator.operation.errors", operation).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Operation_Fails_MarksTheSpanAsErrorAndRethrows(string operation)
    {
        // Arrange
        var @case = Cases()[operation];
        @case.MakeInnerFail();
        using var capture = new TelemetryCapture();

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(@case.Act);

        // Assert
        thrown.ShouldBeSameAs(Failure);
        var activity = capture.Single(operation);
        activity.Status.ShouldBe(ActivityStatusCode.Error);
        activity.StatusDescription.ShouldBe(Failure.Message);
        capture.For(@case.CountInstrument, operation).ShouldHaveSingleItem().Tags["status"].ShouldBe("error");
        capture.For(@case.DurationInstrument, operation).ShouldHaveSingleItem().Tags["status"].ShouldBe("error");
        capture.For("orchestrator.operation.errors", operation).ShouldHaveSingleItem().Value.ShouldBe(1);
    }

    [Fact]
    public async Task ContainerOperations_TagTheSpanWithTheirArguments()
    {
        // Arrange
        _containers.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(new List<ContainerInfo> { new() { Id = "a" }, new() { Id = "b" } }.AsReadOnly());
        _containers.CreateAsync(Arg.Any<CreateContainerRequest>(), Arg.Any<CancellationToken>()).Returns("ctr-new");
        var decorator = new OpenTelemetryContainerManagerDecorator(_containers);
        using var capture = new TelemetryCapture();

        // Act
        await decorator.ListAsync(all: true);
        await decorator.CreateAsync(new CreateContainerRequest { Name = "web", Image = "nginx:1.25" });
        await decorator.StopAsync("ctr-1", TimeSpan.FromSeconds(7));
        await decorator.StopAsync("ctr-2");
        await decorator.RemoveAsync("ctr-1", force: true, removeVolumes: true);
        await decorator.ScaleAsync("ctr-1", 4);

        // Assert
        capture.Single("container.list").GetTagItem("container.list.count").ShouldBe(2);
        var create = capture.Single("container.create");
        create.GetTagItem("container.image").ShouldBe("nginx:1.25");
        create.GetTagItem("container.name").ShouldBe("web");
        create.GetTagItem("container.id").ShouldBe("ctr-new");
        var stops = capture.Activities.Where(a => a.OperationName == "container.stop").ToList();
        stops.Single(a => (string?)a.GetTagItem("container.id") == "ctr-1").GetTagItem("container.stop.timeout_seconds").ShouldBe(7);
        stops.Single(a => (string?)a.GetTagItem("container.id") == "ctr-2").GetTagItem("container.stop.timeout_seconds").ShouldBeNull();
        capture.Single("container.remove").GetTagItem("container.remove.volumes").ShouldBe(true);
        capture.Single("container.scale").GetTagItem("container.scale.replicas").ShouldBe(4);
    }

    [Fact]
    public async Task ContainerLifecycle_RecordsStateChanges()
    {
        // Arrange
        var decorator = new OpenTelemetryContainerManagerDecorator(_containers);
        using var capture = new TelemetryCapture();

        // Act
        await decorator.CreateAsync(new CreateContainerRequest { Image = "nginx:1.25" });
        await decorator.StartAsync("ctr-1");
        await decorator.StopAsync("ctr-1");
        await decorator.RemoveAsync("ctr-1");

        // Assert
        var changes = capture.Measurements.Where(m => m.Instrument == "orchestrator.container.state.changes").ToList();
        changes.Select(m => m.Tags["state"]).ShouldBe(["created", "started", "stopped", "removed"], ignoreOrder: true);
        changes.Single(m => (string?)m.Tags["state"] == "created").Tags["image"].ShouldBe("nginx:1.25");
        changes.Single(m => (string?)m.Tags["state"] == "started").Tags.ContainsKey("image").ShouldBeFalse();
    }

    [Fact]
    public void ContainerManager_PassesHostAddressAndProviderNameThrough()
    {
        // Arrange
        _containers.HostAddress.Returns("10.0.0.5");
        _containers.ProviderName.Returns("Docker");

        // Act
        var decorator = new OpenTelemetryContainerManagerDecorator(_containers);

        // Assert
        decorator.HostAddress.ShouldBe("10.0.0.5");
        decorator.ProviderName.ShouldBe("Docker");
    }

    [Fact]
    public async Task OtherManagers_TagTheSpanWithTheirArguments()
    {
        // Arrange
        _images.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageInfo> { new() }.AsReadOnly());
        _networks.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<NetworkInfo> { new(), new() }.AsReadOnly());
        _networks.CreateAsync("app-net", "overlay", Arg.Any<CancellationToken>()).Returns("net-9");
        _volumes.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<VolumeInfo>().AsReadOnly());
        var images = new OpenTelemetryImageManagerDecorator(_images);
        var networks = new OpenTelemetryNetworkManagerDecorator(_networks);
        var volumes = new OpenTelemetryVolumeManagerDecorator(_volumes);
        using var capture = new TelemetryCapture();

        // Act
        await images.ListAsync();
        await images.PullAsync(new PullImageRequest { Image = "nginx", Tag = "1.25" });
        await images.TagAsync("sha256:abc", "registry/app", "v2");
        await networks.ListAsync();
        await networks.CreateAsync("app-net", "overlay");
        await networks.ConnectAsync("net-1", "ctr-1", ["web", "www"]);
        await networks.ConnectAsync("net-2", "ctr-1");
        await volumes.ListAsync();
        await volumes.CreateAsync("data", "local");
        await volumes.CreateAsync("scratch");

        // Assert
        capture.Single("image.list").GetTagItem("image.list.count").ShouldBe(1);
        capture.Single("image.pull").GetTagItem("image.tag").ShouldBe("1.25");
        capture.Single("image.tag").GetTagItem("image.tag.repository").ShouldBe("registry/app");
        capture.Single("network.list").GetTagItem("network.list.count").ShouldBe(2);
        var create = capture.Single("network.create");
        create.GetTagItem("network.driver").ShouldBe("overlay");
        create.GetTagItem("network.id").ShouldBe("net-9");
        var connects = capture.Activities.Where(a => a.OperationName == "network.connect").ToList();
        connects.Single(a => (string?)a.GetTagItem("network.id") == "net-1").GetTagItem("network.connect.aliases").ShouldBe("web,www");
        connects.Single(a => (string?)a.GetTagItem("network.id") == "net-2").GetTagItem("network.connect.aliases").ShouldBeNull();
        capture.Single("volume.list").GetTagItem("volume.list.count").ShouldBe(0);
        var volumeCreates = capture.Activities.Where(a => a.OperationName == "volume.create").ToList();
        volumeCreates.Single(a => (string?)a.GetTagItem("volume.name") == "data").GetTagItem("volume.driver").ShouldBe("local");
        volumeCreates.Single(a => (string?)a.GetTagItem("volume.name") == "scratch").GetTagItem("volume.driver").ShouldBeNull();
    }

    [Fact]
    public async Task CreateAsync_HostHasOtelVariables_InjectsThemRewritingLoopbackToTheHostAddress()
    {
        // Arrange
        _containers.HostAddress.Returns("host.docker.internal");
        var decorator = new OpenTelemetryContainerManagerDecorator(_containers);
        var request = new CreateContainerRequest { Image = "nginx:1.25" };
        request.EnvironmentVariables["OTEL_SERVICE_NAME"] = "explicit-name";
        var saved = new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"),
            ["OTEL_EXPORTER_OTLP_HEADERS"] = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS"),
            ["OTEL_RESOURCE_ATTRIBUTES"] = Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES"),
            ["OTEL_SERVICE_NAME"] = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            ["OTEL_TRACES_SAMPLER"] = Environment.GetEnvironmentVariable("OTEL_TRACES_SAMPLER"),
        };

        try
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317");
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS", "x-key=1");
            Environment.SetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES", "collector=127.0.0.1");
            Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", "host-name");
            Environment.SetEnvironmentVariable("OTEL_TRACES_SAMPLER", null);

            // Act
            await decorator.CreateAsync(request);
        }
        finally
        {
            foreach (var (name, value) in saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        // Assert
        request.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"].ShouldBe("http://host.docker.internal:4317");
        request.EnvironmentVariables["OTEL_EXPORTER_OTLP_HEADERS"].ShouldBe("x-key=1");
        request.EnvironmentVariables["OTEL_RESOURCE_ATTRIBUTES"].ShouldBe("collector=host.docker.internal");
        request.EnvironmentVariables["OTEL_SERVICE_NAME"].ShouldBe("explicit-name");
        request.EnvironmentVariables.ContainsKey("OTEL_TRACES_SAMPLER").ShouldBeFalse();
    }
}

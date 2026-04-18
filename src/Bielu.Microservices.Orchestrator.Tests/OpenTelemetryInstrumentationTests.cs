using System.Diagnostics;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Docker.Extensions;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.OpenTelemetry;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Extensions;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for the OpenTelemetry tracing decorators.
/// </summary>
public class OpenTelemetryInstrumentationTests
{
    // -----------------------------------------------------------------------
    // DI registration
    // -----------------------------------------------------------------------

    [Fact]
    public void AddOpenTelemetryInstrumentation_WrapsContainerManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(b =>
        {
            b.AddDocker();
            b.AddOpenTelemetryInstrumentation();
        });

       using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IContainerManager>();
        manager.ShouldBeOfType<OpenTelemetryContainerManagerDecorator>();
    }

    [Fact]
    public void AddOpenTelemetryInstrumentation_WrapsImageManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(b =>
        {
            b.AddDocker();
            b.AddOpenTelemetryInstrumentation();
        });

       using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IImageManager>();
        manager.ShouldBeOfType<OpenTelemetryImageManagerDecorator>();
    }

    [Fact]
    public void AddOpenTelemetryInstrumentation_WrapsNetworkManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(b =>
        {
            b.AddDocker();
            b.AddOpenTelemetryInstrumentation();
        });

       using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<INetworkManager>();
        manager.ShouldBeOfType<OpenTelemetryNetworkManagerDecorator>();
    }

    [Fact]
    public void AddOpenTelemetryInstrumentation_WrapsVolumeManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(b =>
        {
            b.AddDocker();
            b.AddOpenTelemetryInstrumentation();
        });

       using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IVolumeManager>();
        manager.ShouldBeOfType<OpenTelemetryVolumeManagerDecorator>();
    }

    // -----------------------------------------------------------------------
    // Decorator delegation — each decorator must forward to the inner manager
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OpenTelemetryContainerManagerDecorator_ListAsync_DelegatesToInner()
    {
        var inner = Substitute.For<IContainerManager>();
        inner.ListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
             .Returns(new List<ContainerInfo>().AsReadOnly());

        var traced = new OpenTelemetryContainerManagerDecorator(inner);
        var result = await traced.ListAsync(all: true);

        result.ShouldNotBeNull();
        await inner.Received(1).ListAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenTelemetryContainerManagerDecorator_CreateAsync_DelegatesToInner()
    {
        const string expectedId = "abc123";
        var inner = Substitute.For<IContainerManager>();
        inner.CreateAsync(Arg.Any<CreateContainerRequest>(), Arg.Any<CancellationToken>())
             .Returns(expectedId);

        var traced = new OpenTelemetryContainerManagerDecorator(inner);
        var request = new CreateContainerRequest { Image = "nginx:latest" };
        var id = await traced.CreateAsync(request);

        id.ShouldBe(expectedId);
        await inner.Received(1).CreateAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenTelemetryContainerManagerDecorator_StartAsync_DelegatesToInner()
    {
        var inner = Substitute.For<IContainerManager>();
        var traced = new OpenTelemetryContainerManagerDecorator(inner);

        await traced.StartAsync("ctr1");

        await inner.Received(1).StartAsync("ctr1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenTelemetryContainerManagerDecorator_StopAsync_DelegatesToInner()
    {
        var inner = Substitute.For<IContainerManager>();
        var traced = new OpenTelemetryContainerManagerDecorator(inner);
        var timeout = TimeSpan.FromSeconds(15);

        await traced.StopAsync("ctr1", timeout);

        await inner.Received(1).StopAsync("ctr1", timeout, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenTelemetryContainerManagerDecorator_RemoveAsync_DelegatesToInner()
    {
        var inner = Substitute.For<IContainerManager>();
        var traced = new OpenTelemetryContainerManagerDecorator(inner);

        await traced.RemoveAsync("ctr1", force: true);

        await inner.Received(1).RemoveAsync("ctr1", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenTelemetryContainerManagerDecorator_GetLogsAsync_DelegatesToInner()
    {
        var inner = Substitute.For<IContainerManager>();
        inner.GetLogsAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
             .Returns("log output");

        var traced = new OpenTelemetryContainerManagerDecorator(inner);
        var logs = await traced.GetLogsAsync("ctr1");

        logs.ShouldBe("log output");
    }

    [Fact]
    public async Task OpenTelemetryContainerManagerDecorator_ScaleAsync_DelegatesToInner()
    {
        var inner = Substitute.For<IContainerManager>();
        var traced = new OpenTelemetryContainerManagerDecorator(inner);

        await traced.ScaleAsync("ctr1", 3);

        await inner.Received(1).ScaleAsync("ctr1", 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenTelemetryImageManagerDecorator_PullAsync_DelegatesToInner()
    {
        var inner = Substitute.For<IImageManager>();
        var traced = new OpenTelemetryImageManagerDecorator(inner);
        var request = new PullImageRequest { Image = "nginx", Tag = "latest" };

        await traced.PullAsync(request);

        await inner.Received(1).PullAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenTelemetryNetworkManagerDecorator_CreateAsync_DelegatesToInner()
    {
        var inner = Substitute.For<INetworkManager>();
        inner.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns("net-id");

        var traced = new OpenTelemetryNetworkManagerDecorator(inner);
        var id = await traced.CreateAsync("my-net", "bridge");

        id.ShouldBe("net-id");
        await inner.Received(1).CreateAsync("my-net", "bridge", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenTelemetryVolumeManagerDecorator_CreateAsync_DelegatesToInner()
    {
        var inner = Substitute.For<IVolumeManager>();
        inner.CreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(new VolumeInfo { Name = "vol1" });

        var traced = new OpenTelemetryVolumeManagerDecorator(inner);
        var vol = await traced.CreateAsync("vol1");

        vol.Name.ShouldBe("vol1");
        await inner.Received(1).CreateAsync("vol1", null, Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Activity source name
    // -----------------------------------------------------------------------

    [Fact]
    public void OrchestratorActivitySource_Name_MatchesAssemblyName()
    {
        OrchestratorActivitySource.Name.ShouldBe("Bielu.Microservices.Orchestrator.OpenTelemetry");
    }

    [Fact]
    public void OrchestratorActivitySource_Source_IsNotNull()
    {
        OrchestratorActivitySource.Source.ShouldNotBeNull();
        OrchestratorActivitySource.Source.Name.ShouldBe(OrchestratorActivitySource.Name);
    }

    // -----------------------------------------------------------------------
    // Decorator ordering — OTel must always be outermost regardless of call order
    // -----------------------------------------------------------------------

    [Fact]
    public void OTelBeforeStateTracking_OTelIsOutermost()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(b =>
        {
            b.AddDocker();
            b.AddOpenTelemetryInstrumentation();
            b.WithStateTracking();
        });

       using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IContainerManager>();
        manager.ShouldBeOfType<OpenTelemetryContainerManagerDecorator>();
    }

    [Fact]
    public void StateTrackingBeforeOTel_OTelIsStillOutermost()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(b =>
        {
            b.AddDocker();
            b.WithStateTracking();
            b.AddOpenTelemetryInstrumentation();
        });

       using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IContainerManager>();
        manager.ShouldBeOfType<OpenTelemetryContainerManagerDecorator>();
    }
}

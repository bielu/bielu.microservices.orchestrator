using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="StateTrackingContainerManagerDecorator"/>.
/// </summary>
public class StateTrackingDecoratorTests
{
    private readonly IContainerManager _inner = Substitute.For<IContainerManager>();
    private readonly IInstanceStore _store = new InMemoryInstanceStore();
    private readonly IContainerOrchestrator _orchestrator = Substitute.For<IContainerOrchestrator>();
    private readonly StateTrackingContainerManagerDecorator _decorator;

    public StateTrackingDecoratorTests()
    {
        _orchestrator.ProviderName.Returns("Docker");
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<StateTrackingContainerManagerDecorator>>();
        _decorator = new StateTrackingContainerManagerDecorator(_inner, _store, _orchestrator, logger);
    }

    // -----------------------------------------------------------------------
    // CreateAsync — persists instance to store
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_DelegatesToInnerAndPersistsInstance()
    {
        var request = new CreateContainerRequest { Name = "web-app", Image = "nginx:latest", Replicas = 2 };
        _inner.CreateAsync(request, Arg.Any<CancellationToken>()).Returns("ctr-123");

        var result = await _decorator.CreateAsync(request);

        result.ShouldBe("ctr-123");
        await _inner.Received(1).CreateAsync(request, Arg.Any<CancellationToken>());

        var stored = await _store.GetAsync("web-app");
        stored.ShouldNotBeNull();
        stored.ContainerIds.ShouldContain("ctr-123");
        stored.DesiredState.ShouldBe(DesiredState.Running);
        stored.DesiredReplicas.ShouldBe(2);
        stored.ProviderName.ShouldBe("Docker");
        stored.OriginalRequest.Image.ShouldBe("nginx:latest");
    }

    [Fact]
    public async Task CreateAsync_UsesContainerIdAsInstanceIdWhenNameIsNull()
    {
        var request = new CreateContainerRequest { Image = "nginx:latest" };
        _inner.CreateAsync(request, Arg.Any<CancellationToken>()).Returns("ctr-456");

        await _decorator.CreateAsync(request);

        var stored = await _store.GetAsync("ctr-456");
        stored.ShouldNotBeNull();
    }

    // -----------------------------------------------------------------------
    // RemoveAsync — removes from store before delegating
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RemoveAsync_DelegatesToInnerAndRemovesFromStore()
    {
        // Pre-populate store
        await _store.SaveAsync(new ManagedInstance
        {
            Id = "web-app",
            ContainerIds = ["ctr-123"],
            OriginalRequest = new CreateContainerRequest { Image = "nginx:latest" },
            DesiredState = DesiredState.Running,
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _decorator.RemoveAsync("web-app", force: true);

        await _inner.Received(1).RemoveAsync("web-app", true, Arg.Any<CancellationToken>());

        var stored = await _store.GetAsync("web-app");
        stored.ShouldBeNull();
    }

    // -----------------------------------------------------------------------
    // ScaleAsync — updates desired replicas in store
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ScaleAsync_DelegatesToInnerAndUpdatesReplicas()
    {
        await _store.SaveAsync(new ManagedInstance
        {
            Id = "web-app",
            DesiredReplicas = 1,
            OriginalRequest = new CreateContainerRequest { Image = "nginx:latest" },
            DesiredState = DesiredState.Running,
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _decorator.ScaleAsync("web-app", 5);

        await _inner.Received(1).ScaleAsync("web-app", 5, Arg.Any<CancellationToken>());

        var stored = await _store.GetAsync("web-app");
        stored.ShouldNotBeNull();
        stored.DesiredReplicas.ShouldBe(5);
    }

    // -----------------------------------------------------------------------
    // Pass-through operations
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_DelegatesToInner()
    {
        _inner.ListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
              .Returns(new List<ContainerInfo>().AsReadOnly());

        var result = await _decorator.ListAsync(all: true);

        result.ShouldNotBeNull();
        await _inner.Received(1).ListAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_DelegatesToInner()
    {
        _inner.GetAsync("ctr-1", Arg.Any<CancellationToken>())
              .Returns(new ContainerInfo { Id = "ctr-1" });

        var result = await _decorator.GetAsync("ctr-1");

        result.ShouldNotBeNull();
        result.Id.ShouldBe("ctr-1");
    }

    [Fact]
    public async Task StartAsync_DelegatesToInner()
    {
        await _decorator.StartAsync("ctr-1");
        await _inner.Received(1).StartAsync("ctr-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_DelegatesToInner()
    {
        var timeout = TimeSpan.FromSeconds(10);
        await _decorator.StopAsync("ctr-1", timeout);
        await _inner.Received(1).StopAsync("ctr-1", timeout, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLogsAsync_DelegatesToInner()
    {
        _inner.GetLogsAsync("ctr-1", true, true, Arg.Any<CancellationToken>()).Returns("logs");

        var result = await _decorator.GetLogsAsync("ctr-1");

        result.ShouldBe("logs");
    }
}

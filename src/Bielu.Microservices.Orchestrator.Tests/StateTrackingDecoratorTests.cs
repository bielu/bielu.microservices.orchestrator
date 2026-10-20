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
    private readonly StateTrackingContainerManagerDecorator _decorator;

    public StateTrackingDecoratorTests()
    {
        _inner.ProviderName.Returns("Docker");
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<StateTrackingContainerManagerDecorator>>();
        var options = new OrchestratorOptions { OrchestratorId = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        _decorator = new StateTrackingContainerManagerDecorator(_inner, _store, options, logger);
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

        await _inner.Received(1).RemoveAsync("web-app", true, false, Arg.Any<CancellationToken>());

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

    [Fact]
    public void HostAddressAndProviderName_ComeFromInner()
    {
        // Arrange
        _inner.HostAddress.Returns("unix:///var/run/docker.sock");

        // Act & Assert
        _decorator.HostAddress.ShouldBe("unix:///var/run/docker.sock");
        _decorator.ProviderName.ShouldBe("Docker");
    }

    // -----------------------------------------------------------------------
    // Desired-state tracking
    // -----------------------------------------------------------------------

    private async Task SeedAsync(string id, DesiredState state)
    {
        await _store.SaveAsync(new ManagedInstance
        {
            Id = id,
            ContainerIds = [id],
            OriginalRequest = new CreateContainerRequest { Image = "nginx:latest" },
            DesiredState = state,
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    [Fact]
    public async Task StopAsync_TrackedInstance_RecordsStoppedDesiredState()
    {
        // Arrange
        await SeedAsync("web-app", DesiredState.Running);

        // Act
        await _decorator.StopAsync("web-app");

        // Assert
        (await _store.GetAsync("web-app"))!.DesiredState.ShouldBe(DesiredState.Stopped);
    }

    [Fact]
    public async Task StartAsync_StoppedInstance_RecordsRunningDesiredState()
    {
        // Arrange
        await SeedAsync("web-app", DesiredState.Stopped);

        // Act
        await _decorator.StartAsync("web-app");

        // Assert
        (await _store.GetAsync("web-app"))!.DesiredState.ShouldBe(DesiredState.Running);
    }

    [Fact]
    public async Task StartAsync_AlreadyRunning_DoesNotRewriteTheRecord()
    {
        // Arrange
        await SeedAsync("web-app", DesiredState.Running);
        var before = (await _store.GetAsync("web-app"))!.UpdatedAt;

        // Act
        await _decorator.StartAsync("web-app");

        // Assert
        (await _store.GetAsync("web-app"))!.UpdatedAt.ShouldBe(before);
    }

    [Fact]
    public async Task ScaleAsync_UntrackedInstance_OnlyDelegates()
    {
        // Act
        await _decorator.ScaleAsync("unknown", 3);

        // Assert
        await _inner.Received(1).ScaleAsync("unknown", 3, Arg.Any<CancellationToken>());
        (await _store.GetAsync("unknown")).ShouldBeNull();
    }

    // -----------------------------------------------------------------------
    // Replica discovery
    // -----------------------------------------------------------------------

    private static ContainerInfo Replica(string id, string? group)
    {
        var info = new ContainerInfo { Id = id };
        if (group != null) info.Labels[OrchestratorLabels.Group] = group;
        return info;
    }

    [Fact]
    public async Task CreateAsync_MultipleReplicas_StoresEveryReplicaOfTheGroup()
    {
        // Arrange
        var request = new CreateContainerRequest { Name = "web-app", Image = "nginx:latest", Replicas = 3 };
        _inner.CreateAsync(request, Arg.Any<CancellationToken>()).Returns("ctr-1");
        _inner.ListAsync(true, Arg.Any<CancellationToken>()).Returns(new List<ContainerInfo>
        {
            Replica("ctr-1", "web-app"), Replica("ctr-2", "web-app"), Replica("ctr-3", "web-app"),
            Replica("other", "another-app"), Replica("loose", null)
        }.AsReadOnly());

        // Act
        await _decorator.CreateAsync(request);

        // Assert
        (await _store.GetAsync("web-app"))!.ContainerIds.ShouldBe(["ctr-1", "ctr-2", "ctr-3"]);
    }

    [Fact]
    public async Task CreateAsync_ListingMissesTheFirstReplica_PrependsIt()
    {
        // Arrange
        var request = new CreateContainerRequest { Name = "web-app", Image = "nginx:latest", Replicas = 2 };
        _inner.CreateAsync(request, Arg.Any<CancellationToken>()).Returns("ctr-1");
        _inner.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(new List<ContainerInfo> { Replica("ctr-2", "web-app") }.AsReadOnly());

        // Act
        await _decorator.CreateAsync(request);

        // Assert
        (await _store.GetAsync("web-app"))!.ContainerIds.ShouldBe(["ctr-1", "ctr-2"]);
    }

    [Fact]
    public async Task CreateAsync_NoReplicasDiscovered_StoresOnlyThePrimaryId()
    {
        // Arrange
        var request = new CreateContainerRequest { Name = "web-app", Image = "nginx:latest", Replicas = 2 };
        _inner.CreateAsync(request, Arg.Any<CancellationToken>()).Returns("ctr-1");
        _inner.ListAsync(true, Arg.Any<CancellationToken>()).Returns(new List<ContainerInfo>().AsReadOnly());

        // Act
        await _decorator.CreateAsync(request);

        // Assert
        (await _store.GetAsync("web-app"))!.ContainerIds.ShouldBe(["ctr-1"]);
    }

    [Fact]
    public async Task CreateAsync_ReplicaDiscoveryFails_StoresOnlyThePrimaryId()
    {
        // Arrange
        var request = new CreateContainerRequest { Name = "web-app", Image = "nginx:latest", Replicas = 2 };
        _inner.CreateAsync(request, Arg.Any<CancellationToken>()).Returns("ctr-1");
        _inner.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<ContainerInfo>>(new InvalidOperationException("list failed")));

        // Act
        await _decorator.CreateAsync(request);

        // Assert
        (await _store.GetAsync("web-app"))!.ContainerIds.ShouldBe(["ctr-1"]);
    }

    // -----------------------------------------------------------------------
    // Store failures never break runtime operations
    // -----------------------------------------------------------------------

    private StateTrackingContainerManagerDecorator CreateWithFailingStore()
    {
        var store = Substitute.For<IInstanceStore>();
        var failure = new IOException("store unavailable");
        store.SaveAsync(Arg.Any<ManagedInstance>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        store.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<ManagedInstance?>(failure));
        store.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<StateTrackingContainerManagerDecorator>>();
        return new StateTrackingContainerManagerDecorator(_inner, store, new OrchestratorOptions(), logger);
    }

    [Fact]
    public async Task CreateAsync_StoreFails_StillReturnsTheContainerId()
    {
        // Arrange
        var decorator = CreateWithFailingStore();
        var request = new CreateContainerRequest { Name = "web-app", Image = "nginx:latest" };
        _inner.CreateAsync(request, Arg.Any<CancellationToken>()).Returns("ctr-1");

        // Act
        var id = await decorator.CreateAsync(request);

        // Assert
        id.ShouldBe("ctr-1");
    }

    [Fact]
    public async Task LifecycleOperations_StoreFails_StillReachTheRuntime()
    {
        // Arrange
        var decorator = CreateWithFailingStore();

        // Act
        await decorator.StartAsync("ctr-1");
        await decorator.StopAsync("ctr-1");
        await decorator.ScaleAsync("ctr-1", 2);
        await decorator.RemoveAsync("ctr-1", force: true, removeVolumes: true);

        // Assert
        await _inner.Received(1).StartAsync("ctr-1", Arg.Any<CancellationToken>());
        await _inner.Received(1).StopAsync("ctr-1", null, Arg.Any<CancellationToken>());
        await _inner.Received(1).ScaleAsync("ctr-1", 2, Arg.Any<CancellationToken>());
        await _inner.Received(1).RemoveAsync("ctr-1", true, true, Arg.Any<CancellationToken>());
    }
}

using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Storage;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="InstanceRecoveryService"/>.
/// </summary>
public class InstanceRecoveryServiceTests
{
    private readonly InMemoryInstanceStore _store = new();
    private readonly IContainerManager _containerManager = Substitute.For<IContainerManager>();
    private readonly InstanceRecoveryService _service;

    public InstanceRecoveryServiceTests()
    {
        var logger = Substitute.For<ILogger<InstanceRecoveryService>>();
        _service = new InstanceRecoveryService(_store, _containerManager, logger);
    }

    [Fact]
    public async Task StartAsync_NoInstances_CompletesWithoutAction()
    {
        await _service.ReconcileAllAsync(CancellationToken.None);

        await _containerManager.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_RemovedInstance_CleansUpStoreRecord()
    {
        await _store.SaveAsync(new ManagedInstance
        {
            Id = "inst-1",
            DesiredState = DesiredState.Removed,
            OriginalRequest = new CreateContainerRequest { Image = "nginx:latest" },
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _service.ReconcileAllAsync(CancellationToken.None);

        var result = await _store.GetAsync("inst-1");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task StartAsync_RunningInstance_ContainersExist_NoRecreation()
    {
        await _store.SaveAsync(new ManagedInstance
        {
            Id = "inst-1",
            ContainerIds = ["ctr-1"],
            DesiredState = DesiredState.Running,
            OriginalRequest = new CreateContainerRequest { Image = "nginx:latest" },
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });

        _containerManager.GetAsync("ctr-1", Arg.Any<CancellationToken>())
            .Returns(new ContainerInfo { Id = "ctr-1", State = ContainerState.Running });

        await _service.ReconcileAllAsync(CancellationToken.None);

        await _containerManager.DidNotReceive().CreateAsync(Arg.Any<CreateContainerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_RunningInstance_ContainerExited_RestartsIt()
    {
        await _store.SaveAsync(new ManagedInstance
        {
            Id = "inst-1",
            ContainerIds = ["ctr-1"],
            DesiredState = DesiredState.Running,
            OriginalRequest = new CreateContainerRequest { Image = "nginx:latest" },
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });

        _containerManager.GetAsync("ctr-1", Arg.Any<CancellationToken>())
            .Returns(new ContainerInfo { Id = "ctr-1", State = ContainerState.Exited });

        await _service.ReconcileAllAsync(CancellationToken.None);

        await _containerManager.Received(1).StartAsync("ctr-1", Arg.Any<CancellationToken>());
        await _containerManager.DidNotReceive().CreateAsync(Arg.Any<CreateContainerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_RunningInstance_ContainerMissing_RecreatesIt()
    {
        var request = new CreateContainerRequest { Image = "nginx:latest", Name = "inst-1" };
        await _store.SaveAsync(new ManagedInstance
        {
            Id = "inst-1",
            ContainerIds = ["ctr-1"],
            DesiredState = DesiredState.Running,
            OriginalRequest = request,
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });

        _containerManager.GetAsync("ctr-1", Arg.Any<CancellationToken>())
            .Returns((ContainerInfo?)null);
        _containerManager.CreateAsync(request, Arg.Any<CancellationToken>())
            .Returns("ctr-new");

        await _service.ReconcileAllAsync(CancellationToken.None);

        await _containerManager.Received(1).CreateAsync(request, Arg.Any<CancellationToken>());
        await _containerManager.Received(1).StartAsync("ctr-new", Arg.Any<CancellationToken>());

        var updated = await _store.GetAsync("inst-1");
        updated.ShouldNotBeNull();
        updated.ContainerIds.ShouldContain("ctr-new");
        updated.ContainerIds.ShouldNotContain("ctr-1");
    }

    [Fact]
    public async Task StartAsync_StoppedInstance_TakesNoAction()
    {
        await _store.SaveAsync(new ManagedInstance
        {
            Id = "inst-1",
            ContainerIds = ["ctr-1"],
            DesiredState = DesiredState.Stopped,
            OriginalRequest = new CreateContainerRequest { Image = "nginx:latest" },
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _service.ReconcileAllAsync(CancellationToken.None);

        await _containerManager.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _containerManager.DidNotReceive().CreateAsync(Arg.Any<CreateContainerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_CompletesImmediately()
    {
        await Should.NotThrowAsync(() => _service.StopAsync(CancellationToken.None));
    }

    private Task SeedRunningAsync(string id, params string[] containerIds) => _store.SaveAsync(new ManagedInstance
    {
        Id = id,
        ContainerIds = containerIds,
        DesiredState = DesiredState.Running,
        OriginalRequest = new CreateContainerRequest { Image = "nginx:latest" },
        ProviderName = "Docker",
        CreatedAt = DateTimeOffset.UtcNow
    });

    [Fact]
    public async Task StartAsync_HostedService_ReconcilesOnStartup()
    {
        // Arrange
        await SeedRunningAsync("inst-1", "ctr-1");
        _containerManager.GetAsync("ctr-1", Arg.Any<CancellationToken>())
            .Returns(new ContainerInfo { Id = "ctr-1", State = ContainerState.Exited });

        // Act
        await _service.StartAsync(CancellationToken.None);
        await _service.ExecuteTask!;

        // Assert
        await _containerManager.Received(1).StartAsync("ctr-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAllAsync_StoreFails_SkipsRecovery()
    {
        // Arrange
        var store = Substitute.For<IInstanceStore>();
        store.GetAllAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("store unavailable"));
        var service = new InstanceRecoveryService(store, _containerManager, Substitute.For<ILogger<InstanceRecoveryService>>());

        // Act
        await Should.NotThrowAsync(() => service.ReconcileAllAsync(CancellationToken.None));

        // Assert
        await _containerManager.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAllAsync_AlreadyCancelled_StopsBeforeTheFirstInstance()
    {
        // Arrange
        await SeedRunningAsync("inst-1", "ctr-1");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        await _service.ReconcileAllAsync(cts.Token);

        // Assert
        await _containerManager.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAllAsync_OneInstanceFails_ContinuesWithTheNext()
    {
        // Arrange
        await SeedRunningAsync("inst-1", "ctr-broken");
        await SeedRunningAsync("inst-2", "ctr-2");
        _containerManager.GetAsync("ctr-broken", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("runtime down"));
        _containerManager.GetAsync("ctr-2", Arg.Any<CancellationToken>())
            .Returns(new ContainerInfo { Id = "ctr-2", State = ContainerState.Created });

        // Act
        await _service.ReconcileAllAsync(CancellationToken.None);

        // Assert
        await _containerManager.Received(1).StartAsync("ctr-2", Arg.Any<CancellationToken>());
    }
}

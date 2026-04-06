using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Storage.EfCore;
using Bielu.Microservices.Orchestrator.Storage.EfCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="EfCoreInstanceStore"/> using the EF Core InMemory provider.
/// </summary>
public class EfCoreInstanceStoreTests : IDisposable
{
    private readonly InstanceStoreDbContext _dbContext;
    private readonly EfCoreInstanceStore _store;

    public EfCoreInstanceStoreTests()
    {
        var options = new DbContextOptionsBuilder<InstanceStoreDbContext>()
            .UseInMemoryDatabase(databaseName: $"orchestrator-test-{Guid.NewGuid()}")
            .Options;

        _dbContext = new InstanceStoreDbContext(options);
        _dbContext.Database.EnsureCreated();
        _store = new EfCoreInstanceStore(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    // -----------------------------------------------------------------------
    // SaveAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SaveAsync_StoresInstance()
    {
        var instance = CreateInstance("inst-1");

        await _store.SaveAsync(instance);

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.Id.ShouldBe("inst-1");
        retrieved.ProviderName.ShouldBe("Docker");
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingInstance()
    {
        var instance = CreateInstance("inst-1");
        instance.DesiredReplicas = 1;
        await _store.SaveAsync(instance);

        instance.DesiredReplicas = 5;
        await _store.SaveAsync(instance);

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.DesiredReplicas.ShouldBe(5);
    }

    [Fact]
    public async Task SaveAsync_SetsUpdatedAt()
    {
        var instance = CreateInstance("inst-1");
        var before = DateTimeOffset.UtcNow;

        await _store.SaveAsync(instance);

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public async Task SaveAsync_ThrowsOnNull()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => _store.SaveAsync(null!));
    }

    [Fact]
    public async Task SaveAsync_ThrowsOnEmptyId()
    {
        var instance = new ManagedInstance { Id = "" };
        await Should.ThrowAsync<ArgumentException>(() => _store.SaveAsync(instance));
    }

    [Fact]
    public async Task SaveAsync_PersistsOriginalRequest()
    {
        var instance = CreateInstance("inst-1");
        instance.OriginalRequest = new CreateContainerRequest
        {
            Name = "web-app",
            Image = "nginx:alpine",
            Replicas = 3,
            AutoRemove = true
        };
        instance.OriginalRequest.EnvironmentVariables["ENV"] = "prod";
        instance.OriginalRequest.Ports.Add(new PortMapping { ContainerPort = 80, HostPort = 8080 });

        await _store.SaveAsync(instance);

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.OriginalRequest.Name.ShouldBe("web-app");
        retrieved.OriginalRequest.Image.ShouldBe("nginx:alpine");
        retrieved.OriginalRequest.Replicas.ShouldBe(3);
        retrieved.OriginalRequest.AutoRemove.ShouldBeTrue();
        retrieved.OriginalRequest.EnvironmentVariables["ENV"].ShouldBe("prod");
        retrieved.OriginalRequest.Ports.Count.ShouldBe(1);
        retrieved.OriginalRequest.Ports[0].ContainerPort.ShouldBe(80);
    }

    [Fact]
    public async Task SaveAsync_PersistsContainerIds()
    {
        var instance = CreateInstance("inst-1");
        instance.ContainerIds = ["ctr-a", "ctr-b", "ctr-c"];

        await _store.SaveAsync(instance);

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.ContainerIds.Count.ShouldBe(3);
        retrieved.ContainerIds[0].ShouldBe("ctr-a");
        retrieved.ContainerIds[2].ShouldBe("ctr-c");
    }

    [Fact]
    public async Task SaveAsync_PersistsMetadata()
    {
        var instance = CreateInstance("inst-1");
        instance.Metadata["team"] = "platform";
        instance.Metadata["env"] = "production";

        await _store.SaveAsync(instance);

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.Metadata.Count.ShouldBe(2);
        retrieved.Metadata["team"].ShouldBe("platform");
        retrieved.Metadata["env"].ShouldBe("production");
    }

    // -----------------------------------------------------------------------
    // GetAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ReturnsNullWhenNotFound()
    {
        var result = await _store.GetAsync("nonexistent");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_ThrowsOnEmptyId()
    {
        await Should.ThrowAsync<ArgumentException>(() => _store.GetAsync(""));
    }

    // -----------------------------------------------------------------------
    // GetAllAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyWhenNoInstances()
    {
        var result = await _store.GetAllAsync();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllInstances()
    {
        await _store.SaveAsync(CreateInstance("inst-1"));
        await _store.SaveAsync(CreateInstance("inst-2"));
        await _store.SaveAsync(CreateInstance("inst-3"));

        var result = await _store.GetAllAsync();
        result.Count.ShouldBe(3);
    }

    // -----------------------------------------------------------------------
    // RemoveAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RemoveAsync_RemovesInstance()
    {
        await _store.SaveAsync(CreateInstance("inst-1"));

        await _store.RemoveAsync("inst-1");

        var result = await _store.GetAsync("inst-1");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrowWhenNotFound()
    {
        await Should.NotThrowAsync(() => _store.RemoveAsync("nonexistent"));
    }

    [Fact]
    public async Task RemoveAsync_ThrowsOnEmptyId()
    {
        await Should.ThrowAsync<ArgumentException>(() => _store.RemoveAsync(""));
    }

    // -----------------------------------------------------------------------
    // UpdateContainerIdsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateContainerIdsAsync_UpdatesContainerIds()
    {
        var instance = CreateInstance("inst-1");
        instance.ContainerIds = ["old-ctr"];
        await _store.SaveAsync(instance);

        var newIds = new List<string> { "new-ctr-1", "new-ctr-2" }.AsReadOnly();
        await _store.UpdateContainerIdsAsync("inst-1", newIds);

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.ContainerIds.Count.ShouldBe(2);
        retrieved.ContainerIds[0].ShouldBe("new-ctr-1");
        retrieved.ContainerIds[1].ShouldBe("new-ctr-2");
    }

    [Fact]
    public async Task UpdateContainerIdsAsync_SetsUpdatedAt()
    {
        var instance = CreateInstance("inst-1");
        await _store.SaveAsync(instance);

        var before = DateTimeOffset.UtcNow;
        await _store.UpdateContainerIdsAsync("inst-1", new List<string> { "ctr-new" }.AsReadOnly());

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public async Task UpdateContainerIdsAsync_NoOpWhenNotFound()
    {
        await Should.NotThrowAsync(() =>
            _store.UpdateContainerIdsAsync("nonexistent", new List<string>().AsReadOnly()));
    }

    [Fact]
    public async Task UpdateContainerIdsAsync_ThrowsOnEmptyId()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            _store.UpdateContainerIdsAsync("", new List<string>().AsReadOnly()));
    }

    [Fact]
    public async Task UpdateContainerIdsAsync_ThrowsOnNullContainerIds()
    {
        await Should.ThrowAsync<ArgumentNullException>(() =>
            _store.UpdateContainerIdsAsync("inst-1", null!));
    }

    // -----------------------------------------------------------------------
    // DI Registration
    // -----------------------------------------------------------------------

    [Fact]
    public void UseEfCoreInstanceStore_RegistersEfCoreStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.UseEfCoreInstanceStore(opts =>
                opts.UseInMemoryDatabase($"di-test-{Guid.NewGuid()}"));
        });

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetService<IInstanceStore>();
        store.ShouldNotBeNull();
        store.ShouldBeOfType<EfCoreInstanceStore>();
    }

    [Fact]
    public void UseEfCoreInstanceStore_RegistersDbContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.UseEfCoreInstanceStore(opts =>
                opts.UseInMemoryDatabase($"di-test-{Guid.NewGuid()}"));
        });

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetService<InstanceStoreDbContext>();
        dbContext.ShouldNotBeNull();
    }

    [Fact]
    public void UseEfCoreInstanceStore_ReturnsBuilderForChaining()
    {
        var options = new OrchestratorOptions();
        var builder = new OrchestratorBuilder(new ServiceCollection(), options);

        var result = builder.UseEfCoreInstanceStore(opts =>
            opts.UseInMemoryDatabase("chaining-test"));

        result.ShouldBeSameAs(builder);
    }

    [Fact]
    public void UseEfCoreInstanceStore_ThrowsOnNullConfigDelegate()
    {
        var options = new OrchestratorOptions();
        var builder = new OrchestratorBuilder(new ServiceCollection(), options);

        Should.Throw<ArgumentNullException>(() =>
            builder.UseEfCoreInstanceStore(null!));
    }

    // -----------------------------------------------------------------------
    // Entity mapping
    // -----------------------------------------------------------------------

    [Fact]
    public void ManagedInstanceEntity_RoundTrips_DomainModel()
    {
        var original = CreateInstance("inst-1");
        original.ContainerIds = ["ctr-1", "ctr-2"];
        original.OriginalRequest.Ports.Add(new PortMapping { ContainerPort = 80, HostPort = 8080 });
        original.Metadata["key"] = "value";

        var entity = ManagedInstanceEntity.FromDomainModel(original);
        var roundTripped = entity.ToDomainModel();

        roundTripped.Id.ShouldBe(original.Id);
        roundTripped.ContainerIds.Count.ShouldBe(2);
        roundTripped.OriginalRequest.Image.ShouldBe("nginx:latest");
        roundTripped.OriginalRequest.Ports.Count.ShouldBe(1);
        roundTripped.DesiredState.ShouldBe(DesiredState.Running);
        roundTripped.DesiredReplicas.ShouldBe(1);
        roundTripped.ProviderName.ShouldBe("Docker");
        roundTripped.Metadata["key"].ShouldBe("value");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ManagedInstance CreateInstance(string id) => new()
    {
        Id = id,
        OriginalRequest = new CreateContainerRequest { Image = "nginx:latest", Name = id },
        DesiredState = DesiredState.Running,
        DesiredReplicas = 1,
        ProviderName = "Docker",
        CreatedAt = DateTimeOffset.UtcNow
    };
}

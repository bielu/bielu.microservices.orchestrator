using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Storage;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="InMemoryInstanceStore"/>.
/// </summary>
public class InMemoryInstanceStoreTests
{
    private readonly InMemoryInstanceStore _store = new();

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
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingInstance()
    {
        var instance = CreateInstance("inst-1");
        instance.DesiredReplicas = 1;
        await _store.SaveAsync(instance);

        instance.DesiredReplicas = 3;
        await _store.SaveAsync(instance);

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.DesiredReplicas.ShouldBe(3);
    }

    [Fact]
    public async Task SaveAsync_SetsUpdatedAt()
    {
        var instance = CreateInstance("inst-1");
        var before = DateTimeOffset.UtcNow;

        await _store.SaveAsync(instance);

        instance.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
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
        instance.ContainerIds = ["old-ctr-1"];
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

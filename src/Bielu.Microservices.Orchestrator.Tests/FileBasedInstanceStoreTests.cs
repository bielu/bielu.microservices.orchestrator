using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Storage.File;
using Bielu.Microservices.Orchestrator.Storage.File.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="FileBasedInstanceStore"/>.
/// </summary>
public class FileBasedInstanceStoreTests : IDisposable
{
    private readonly string _tempFile;
    private readonly FileBasedInstanceStore _store;

    public FileBasedInstanceStoreTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"orchestrator-test-{Guid.NewGuid()}.json");
        var options = new FileInstanceStoreOptions { FilePath = _tempFile };
        var logger = Substitute.For<ILogger<FileBasedInstanceStore>>();
        _store = new FileBasedInstanceStore(options, logger);
    }

    public void Dispose()
    {
        if (System.IO.File.Exists(_tempFile))
        {
            System.IO.File.Delete(_tempFile);
        }
    }

    [Fact]
    public async Task SaveAsync_PersistsToFile()
    {
        var instance = CreateInstance("inst-1");

        await _store.SaveAsync(instance);

        System.IO.File.Exists(_tempFile).ShouldBeTrue();
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

        instance.DesiredReplicas = 5;
        await _store.SaveAsync(instance);

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.DesiredReplicas.ShouldBe(5);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenNotFound()
    {
        var result = await _store.GetAsync("nonexistent");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllInstances()
    {
        await _store.SaveAsync(CreateInstance("inst-1"));
        await _store.SaveAsync(CreateInstance("inst-2"));

        var result = await _store.GetAllAsync();
        result.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RemoveAsync_RemovesInstanceFromFile()
    {
        await _store.SaveAsync(CreateInstance("inst-1"));
        await _store.SaveAsync(CreateInstance("inst-2"));

        await _store.RemoveAsync("inst-1");

        var result = await _store.GetAsync("inst-1");
        result.ShouldBeNull();

        var remaining = await _store.GetAllAsync();
        remaining.Count.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateContainerIdsAsync_UpdatesAndPersists()
    {
        var instance = CreateInstance("inst-1");
        instance.ContainerIds = ["old-ctr"];
        await _store.SaveAsync(instance);

        await _store.UpdateContainerIdsAsync("inst-1", new List<string> { "new-ctr-1", "new-ctr-2" }.AsReadOnly());

        var retrieved = await _store.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.ContainerIds.Count.ShouldBe(2);
        retrieved.ContainerIds[0].ShouldBe("new-ctr-1");
    }

    [Fact]
    public async Task StatePersistedAcrossNewStoreInstances()
    {
        await _store.SaveAsync(CreateInstance("inst-1"));

        // Create a new store instance pointing to the same file
        var options = new FileInstanceStoreOptions { FilePath = _tempFile };
        var logger = Substitute.For<ILogger<FileBasedInstanceStore>>();
        var newStore = new FileBasedInstanceStore(options, logger);

        var retrieved = await newStore.GetAsync("inst-1");
        retrieved.ShouldNotBeNull();
        retrieved.Id.ShouldBe("inst-1");
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

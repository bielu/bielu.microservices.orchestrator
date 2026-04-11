using System.Text.Json;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Storage.File.Configuration;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Storage.File;

/// <summary>
/// File-based implementation of <see cref="IInstanceStore"/> that persists
/// instance state as JSON on disk. Suitable for single-node deployments.
/// </summary>
public class FileBasedInstanceStore(
    FileInstanceStoreOptions options,
    ILogger<FileBasedInstanceStore> logger) : IInstanceStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc />
    public async Task SaveAsync(ManagedInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(instance.Id);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var instances = await LoadFromFileAsync(cancellationToken);
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            instances[instance.Id] = instance;
            await SaveToFileAsync(instances, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ManagedInstance?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var instances = await LoadFromFileAsync(cancellationToken);
            instances.TryGetValue(id, out var instance);
            return instance;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedInstance>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var instances = await LoadFromFileAsync(cancellationToken);
            return instances.Values.ToList().AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var instances = await LoadFromFileAsync(cancellationToken);
            if (instances.Remove(id))
            {
                await SaveToFileAsync(instances, cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateContainerIdsAsync(string id, IReadOnlyList<string> containerIds, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(containerIds);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var instances = await LoadFromFileAsync(cancellationToken);
            if (instances.TryGetValue(id, out var instance))
            {
                instance.ContainerIds = containerIds.ToList();
                instance.UpdatedAt = DateTimeOffset.UtcNow;
                await SaveToFileAsync(instances, cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, ManagedInstance>> LoadFromFileAsync(CancellationToken cancellationToken)
    {
        if (!System.IO.File.Exists(options.FilePath))
        {
            return new Dictionary<string, ManagedInstance>();
        }

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(options.FilePath, cancellationToken);
            return JsonSerializer.Deserialize<Dictionary<string, ManagedInstance>>(json, JsonOptions)
                   ?? new Dictionary<string, ManagedInstance>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read instance store file at {FilePath}; starting with empty state", options.FilePath);
            return new Dictionary<string, ManagedInstance>();
        }
    }

    private async Task SaveToFileAsync(Dictionary<string, ManagedInstance> instances, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(options.FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(instances, JsonOptions);
        await System.IO.File.WriteAllTextAsync(options.FilePath, json, cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

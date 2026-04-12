using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Gateway.Services;

/// <summary>
/// Represents a registered orchestrator instance.
/// </summary>
public sealed class RegisteredInstance
{
    /// <summary>Unique identifier of the instance.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Publicly reachable base URL.</summary>
    public required string Address { get; init; }

    /// <summary>Container runtime provider name.</summary>
    public string? Provider { get; init; }

    /// <summary>Latest CPU usage percentage.</summary>
    public double CpuPercent { get; set; }

    /// <summary>Latest memory usage in MB.</summary>
    public double MemoryMb { get; set; }

    /// <summary>UTC timestamp when this entry expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Thread-safe in-memory store of registered orchestrator instances.
/// Raises <see cref="OnChange"/> when the set of live instances changes.
/// </summary>
public sealed class OrchestratorRegistrationStore(ILogger<OrchestratorRegistrationStore> logger)
{
    private readonly ConcurrentDictionary<string, RegisteredInstance> _instances = new();

    /// <summary>
    /// Raised when instances are added, removed, or updated.
    /// Subscribers (e.g. the YARP config provider) use this to reload proxy configuration.
    /// </summary>
    public event Action? OnChange;

    /// <summary>
    /// Registers a new orchestrator instance or updates an existing one.
    /// </summary>
    /// <returns><c>true</c> if a new instance was added; <c>false</c> if it was updated.</returns>
    public bool Register(RegisteredInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var isNew = !_instances.ContainsKey(instance.InstanceId);
        _instances[instance.InstanceId] = instance;

        logger.LogInformation(
            "Instance {InstanceId} {Action} at {Address}",
            instance.InstanceId,
            isNew ? "registered" : "re-registered",
            instance.Address);

        OnChange?.Invoke();
        return isNew;
    }

    /// <summary>
    /// Refreshes the TTL and resource stats for an existing instance.
    /// </summary>
    /// <returns><c>true</c> if the instance was found and updated.</returns>
    public bool Heartbeat(string instanceId, double cpuPercent, double memoryMb, DateTimeOffset expiresAt)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return false;

        instance.CpuPercent = cpuPercent;
        instance.MemoryMb = memoryMb;
        instance.ExpiresAt = expiresAt;

        // No OnChange needed — resource stats update doesn't change the destination set.
        return true;
    }

    /// <summary>
    /// Removes an orchestrator instance.
    /// </summary>
    /// <returns><c>true</c> if the instance existed and was removed.</returns>
    public bool Deregister(string instanceId)
    {
        if (!_instances.TryRemove(instanceId, out _))
            return false;

        logger.LogInformation("Instance {InstanceId} deregistered", instanceId);
        OnChange?.Invoke();
        return true;
    }

    /// <summary>
    /// Removes all instances whose TTL has expired.
    /// </summary>
    /// <returns>The number of expired instances removed.</returns>
    public int RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var removed = 0;

        foreach (var kvp in _instances)
        {
            if (kvp.Value.ExpiresAt <= now && _instances.TryRemove(kvp.Key, out _))
            {
                logger.LogWarning("Instance {InstanceId} expired (TTL exceeded)", kvp.Key);
                removed++;
            }
        }

        if (removed > 0)
            OnChange?.Invoke();

        return removed;
    }

    /// <summary>
    /// Returns a snapshot of all currently alive instances.
    /// </summary>
    public IReadOnlyList<RegisteredInstance> GetAlive() =>
        _instances.Values.Where(i => i.ExpiresAt > DateTimeOffset.UtcNow).ToList();
}

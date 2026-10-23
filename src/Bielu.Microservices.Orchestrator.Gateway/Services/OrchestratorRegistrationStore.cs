using System.Collections.Concurrent;
using Bielu.Microservices.Orchestrator.Utilities;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Gateway.Services;

/// <summary>
/// Immutable snapshot of a registered orchestrator instance.
/// Updates replace the stored snapshot atomically instead of mutating it.
/// </summary>
public sealed record RegisteredInstance
{
    /// <summary>Unique identifier of the instance.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Publicly reachable base URL.</summary>
    public required string Address { get; init; }

    /// <summary>Container runtime provider name.</summary>
    public string? Provider { get; init; }

    /// <summary>Latest CPU usage percentage.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Latest memory usage in MB.</summary>
    public double MemoryMb { get; init; }

    /// <summary>UTC timestamp when this entry expires.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Thread-safe in-memory store of registered orchestrator instances.
/// Raises <see cref="OnChange"/> when the set of live instances changes.
/// </summary>
public sealed class OrchestratorRegistrationStore(ILogger<OrchestratorRegistrationStore> logger)
{
    private readonly ConcurrentDictionary<string, RegisteredInstance> _instances = new();

    /// <summary>
    /// Raised when instances are added, removed, or re-registered.
    /// Subscribers (e.g. the YARP config provider) use this to reload proxy configuration.
    /// Heartbeats do not raise it; read live stats via <see cref="TryGet"/>.
    /// </summary>
    public event Action? OnChange;

    /// <summary>
    /// Registers a new orchestrator instance or replaces an existing one.
    /// </summary>
    /// <returns><c>true</c> if a new instance was added; <c>false</c> if it was replaced.</returns>
    public bool Register(RegisteredInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var isNew = true;
        _instances.AddOrUpdate(
            instance.InstanceId,
            instance,
            (_, _) =>
            {
                isNew = false;
                return instance;
            });

        var sanitizedId = LogSanitizer.Sanitize(instance.InstanceId);
        var sanitizedAddress = LogSanitizer.Sanitize(instance.Address);
        logger.LogInformation(
            "Instance {InstanceId} {Action} at {Address}",
            sanitizedId,
            isNew ? "registered" : "re-registered",
            sanitizedAddress);

        OnChange?.Invoke();
        return isNew;
    }

    /// <summary>
    /// Refreshes the TTL and resource stats for an existing instance.
    /// </summary>
    /// <returns><c>true</c> if the instance was found and updated.</returns>
    public bool Heartbeat(string instanceId, double cpuPercent, double memoryMb, DateTimeOffset expiresAt)
    {
        // Compare-and-swap so a concurrent re-register or expiry is never overwritten
        while (_instances.TryGetValue(instanceId, out var current))
        {
            var updated = current with
            {
                CpuPercent = cpuPercent,
                MemoryMb = memoryMb,
                ExpiresAt = expiresAt
            };

            if (_instances.TryUpdate(instanceId, updated, current))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Removes an orchestrator instance.
    /// </summary>
    /// <returns><c>true</c> if the instance existed and was removed.</returns>
    public bool Deregister(string instanceId)
    {
        if (!_instances.TryRemove(instanceId, out _))
            return false;

        logger.LogInformation("Instance {InstanceId} deregistered", LogSanitizer.Sanitize(instanceId));
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

        var expired = _instances
            .Where(kvp => kvp.Value.ExpiresAt <= now)
            .ToList();

        foreach (var entry in expired)
        {
            // Only removes the exact snapshot we saw, so an instance refreshed by a
            // concurrent heartbeat or re-register survives.
            if (_instances.TryRemove(entry))
            {
                logger.LogWarning("Instance {InstanceId} expired (TTL exceeded)",
                    LogSanitizer.Sanitize(entry.Key));
                removed++;
            }
        }

        if (removed > 0)
            OnChange?.Invoke();

        return removed;
    }

    /// <summary>
    /// Gets the latest snapshot of an instance, including stats from its most recent heartbeat.
    /// </summary>
    public bool TryGet(string instanceId, out RegisteredInstance instance) =>
        _instances.TryGetValue(instanceId, out instance!);

    /// <summary>
    /// Returns a snapshot of all currently alive instances.
    /// </summary>
    public IReadOnlyList<RegisteredInstance> GetAlive()
    {
        var now = DateTimeOffset.UtcNow;
        return _instances.Values.Where(i => i.ExpiresAt > now).ToList();
    }
}

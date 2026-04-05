using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Decorator for <see cref="IVolumeManager"/> that adds OpenTelemetry tracing to all operations.
/// </summary>
public class TracedVolumeManager : IVolumeManager
{
    private readonly IVolumeManager _inner;

    public TracedVolumeManager(IVolumeManager inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.VolumeList);

        var result = await _inner.ListAsync(cancellationToken);
        activity?.SetTag("volume.list.count", result.Count);
        return result;
    }

    /// <inheritdoc />
    public async Task<VolumeInfo> CreateAsync(string name, string? driver = null, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.VolumeCreate);
        activity?.SetTag(OrchestratorActivitySource.AttributeVolumeName, name);
        if (driver != null)
        {
            activity?.SetTag(OrchestratorActivitySource.AttributeVolumeDriver, driver);
        }

        return await _inner.CreateAsync(name, driver, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string name, bool force = false, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.VolumeRemove);
        activity?.SetTag(OrchestratorActivitySource.AttributeVolumeName, name);
        activity?.SetTag("volume.remove.force", force);

        await _inner.RemoveAsync(name, force, cancellationToken);
    }
}

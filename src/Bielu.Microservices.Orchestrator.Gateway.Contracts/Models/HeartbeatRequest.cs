namespace Bielu.Microservices.Orchestrator.Gateway.Contracts.Models;

/// <summary>
/// Payload sent by an orchestrator instance to refresh its TTL and update resource stats.
/// </summary>
public sealed class HeartbeatRequest
{
    /// <summary>
    /// CPU usage percentage of the orchestrator process since its previous report,
    /// normalized across all logical processors (0–100).
    /// </summary>
    public double CpuPercent { get; init; }

    /// <summary>
    /// Current memory usage in megabytes of the orchestrator process.
    /// </summary>
    public double MemoryMb { get; init; }
}

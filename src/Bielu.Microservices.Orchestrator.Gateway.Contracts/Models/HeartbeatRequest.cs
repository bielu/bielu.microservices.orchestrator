namespace Bielu.Microservices.Orchestrator.Gateway.Contracts.Models;

/// <summary>
/// Payload sent by an orchestrator instance to refresh its TTL and update resource stats.
/// </summary>
public sealed class HeartbeatRequest
{
    /// <summary>
    /// Current CPU usage percentage of the orchestrator host.
    /// </summary>
    public double CpuPercent { get; init; }

    /// <summary>
    /// Current memory usage in megabytes of the orchestrator process.
    /// </summary>
    public double MemoryMb { get; init; }
}

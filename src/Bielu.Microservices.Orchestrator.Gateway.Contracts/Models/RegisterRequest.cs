namespace Bielu.Microservices.Orchestrator.Gateway.Contracts.Models;

/// <summary>
/// Payload sent by an orchestrator instance to register itself with the gateway.
/// </summary>
public sealed class RegisterRequest
{
    /// <summary>
    /// Unique identifier for this orchestrator instance.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// The publicly reachable base URL of the orchestrator instance (e.g. "http://host:5000").
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// The container runtime provider name (e.g. "Docker", "Podman", "Kubernetes").
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Current CPU usage percentage of the orchestrator host.
    /// </summary>
    public double CpuPercent { get; init; }

    /// <summary>
    /// Current memory usage in megabytes of the orchestrator process.
    /// </summary>
    public double MemoryMb { get; init; }
}

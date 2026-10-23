namespace Bielu.Microservices.Orchestrator.Gateway.Contracts.Models;

/// <summary>
/// Response returned by the gateway after a successful registration.
/// </summary>
public sealed class RegisterResponse
{
    /// <summary>
    /// Time-to-live in seconds. The orchestrator must heartbeat before this expires
    /// or the gateway will deregister the instance.
    /// </summary>
    public int TtlSeconds { get; init; }
}

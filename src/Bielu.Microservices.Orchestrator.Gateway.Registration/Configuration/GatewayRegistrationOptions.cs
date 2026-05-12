namespace Bielu.Microservices.Orchestrator.Gateway.Registration.Configuration;

/// <summary>
/// Configuration options for the gateway registration client.
/// </summary>
public sealed class GatewayRegistrationOptions
{
    /// <summary>
    /// Base URL of the gateway (e.g. "http://gateway:8080").
    /// </summary>
    public string GatewayUrl { get; set; } = string.Empty;

    /// <summary>
    /// The publicly reachable URL of this orchestrator instance
    /// (e.g. "http://my-orchestrator:5000").
    /// </summary>
    public string InstanceAddress { get; set; } = string.Empty;

    /// <summary>
    /// API key used to authenticate with the gateway.
    /// Must match the key configured on the gateway side.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Interval between heartbeat calls. Defaults to 10 seconds (roughly TTL / 3).
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Unique identifier for this orchestrator instance. Auto-generated if not set.
    /// </summary>
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
}

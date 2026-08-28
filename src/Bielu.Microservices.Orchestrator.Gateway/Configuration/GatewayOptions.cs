namespace Bielu.Microservices.Orchestrator.Gateway.Configuration;

/// <summary>
/// Configuration options for the YARP gateway.
/// </summary>
public sealed class GatewayOptions
{
    /// <summary>
    /// Time-to-live in seconds for registered instances. If an orchestrator does not
    /// heartbeat within this period, it is automatically deregistered.
    /// </summary>
    public int InstanceTtlSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum memory (MB) used for normalizing the memory fraction in load-balancing score.
    /// </summary>
    public double MaxMemoryMbForScoring { get; set; } = 4096;

    /// <summary>
    /// API key that orchestrator instances must present to register/heartbeat.
    /// Must be set; registration will be rejected if the key does not match.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The YARP route match pattern. Defaults to <c>{**catch-all}</c> to proxy all traffic.
    /// </summary>
    public string RoutePattern { get; set; } = "{**catch-all}";
}

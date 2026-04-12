namespace Bielu.Microservices.Orchestrator.Gateway.Contracts;

/// <summary>
/// Shared constants for gateway communication.
/// </summary>
public static class GatewayConstants
{
    /// <summary>
    /// HTTP header name used to transmit the API key for gateway authentication.
    /// </summary>
    public const string ApiKeyHeaderName = "X-Orchestrator-ApiKey";
}

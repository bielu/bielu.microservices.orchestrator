using System.Text.RegularExpressions;

namespace Bielu.Microservices.Orchestrator.Gateway.Contracts;

/// <summary>
/// Shared constants for gateway communication.
/// </summary>
public static partial class GatewayConstants
{
    /// <summary>
    /// HTTP header name used to transmit the API key for gateway authentication.
    /// </summary>
    public const string ApiKeyHeaderName = "X-Orchestrator-ApiKey";

    /// <summary>
    /// Maximum length of an orchestrator instance identifier.
    /// </summary>
    public const int MaxInstanceIdLength = 128;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="instanceId"/> is non-empty, at most
    /// <see cref="MaxInstanceIdLength"/> characters, and only contains characters that are
    /// safe in a URL path segment (letters, digits, <c>-</c>, <c>_</c> and <c>.</c>).
    /// </summary>
    public static bool IsValidInstanceId(string? instanceId) =>
        !string.IsNullOrEmpty(instanceId)
        && instanceId.Length <= MaxInstanceIdLength
        && InstanceIdRegex().IsMatch(instanceId);

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex InstanceIdRegex();
}

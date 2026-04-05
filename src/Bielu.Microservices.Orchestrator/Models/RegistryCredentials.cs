namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Credentials for authenticating with a container registry.
/// </summary>
public class RegistryCredentials
{
    /// <summary>
    /// The registry server address.
    /// </summary>
    public string ServerAddress { get; set; } = string.Empty;

    /// <summary>
    /// The username for authentication.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The password for authentication.
    /// </summary>
    public string Password { get; set; } = string.Empty;
}

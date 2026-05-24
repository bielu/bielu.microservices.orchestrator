namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Controls when a container is automatically restarted by the runtime.
/// </summary>
public enum RestartPolicy
{
    /// <summary>Never restart automatically (default).</summary>
    No,

    /// <summary>
    /// Always restart, regardless of exit code.
    /// The container is also restarted when the Docker daemon restarts.
    /// </summary>
    Always,

    /// <summary>
    /// Restart always, except when the container was explicitly stopped by the user.
    /// Unlike <see cref="Always"/>, does not restart after a manual <c>docker stop</c>.
    /// </summary>
    UnlessStopped,

    /// <summary>
    /// Restart only when the container exits with a non-zero status code.
    /// Use <see cref="CreateContainerRequest.MaxRestartRetries"/> to cap retry attempts.
    /// </summary>
    OnFailure
}

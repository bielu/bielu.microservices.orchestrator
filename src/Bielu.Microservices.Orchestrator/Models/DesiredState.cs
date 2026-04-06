namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents the desired lifecycle state of a managed instance.
/// </summary>
public enum DesiredState
{
    /// <summary>
    /// The instance should be running.
    /// </summary>
    Running,

    /// <summary>
    /// The instance should be stopped but not removed.
    /// </summary>
    Stopped,

    /// <summary>
    /// The instance should be removed entirely.
    /// </summary>
    Removed
}

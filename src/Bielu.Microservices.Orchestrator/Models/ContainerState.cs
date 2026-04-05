namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents the state of a container.
/// </summary>
public enum ContainerState
{
    Unknown,
    Created,
    Running,
    Paused,
    Restarting,
    Removing,
    Exited,
    Dead
}

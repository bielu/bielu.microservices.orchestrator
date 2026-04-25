namespace Bielu.Microservices.Orchestrator;

/// <summary>
/// Well-known label keys applied to containers managed by the orchestrator.
/// </summary>
public static class OrchestratorLabels
{
    /// <summary>
    /// Label key that identifies the orchestrator that created a container.
    /// </summary>
    public const string ManagedBy = "orchestrator.managed-by";

    /// <summary>
    /// The value written to <see cref="ManagedBy"/> for containers created by this orchestrator.
    /// </summary>
    public const string ManagedByValue = "bielu-orchestrator";

    /// <summary>
    /// Label key for the replica group name when multiple replicas are created.
    /// </summary>
    public const string Group = "orchestrator.group";

    /// <summary>
    /// Label key for the zero-based replica index within a group.
    /// </summary>
    public const string ReplicaIndex = "orchestrator.replica-index";

    public const string ManagedById  = "orchestrator.managed-by-id";
}

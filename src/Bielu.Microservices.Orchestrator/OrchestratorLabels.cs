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

    /// <summary>
    /// Label key for the image reference (e.g. "nginx:1.25") the container was created from.
    /// </summary>
    public const string Image = "orchestrator.image";

    /// <summary>
    /// Label key for the immutable image digest (e.g. "sha256:...") the container was
    /// created from. Used to detect when a newer image is available.
    /// </summary>
    public const string ImageDigest = "orchestrator.image-digest";

    /// <summary>
    /// Label key for the <see cref="Models.ManagedInstance.Id"/> a container belongs to.
    /// Allows correlating a runtime container back to its persisted desired state.
    /// </summary>
    public const string InstanceId = "orchestrator.instance-id";
}

namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents a tracked orchestration unit whose desired state is persisted
/// so the orchestrator can recover and reconcile after a restart.
/// </summary>
public class ManagedInstance
{
    /// <summary>
    /// Unique instance or group identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Runtime container IDs associated with this instance (for reconnection).
    /// </summary>
    public IList<string> ContainerIds { get; set; } = new List<string>();

    /// <summary>
    /// The original creation request, retained so the orchestrator can re-create
    /// containers after a crash or runtime restart.
    /// </summary>
    public CreateContainerRequest OriginalRequest { get; set; } = new();

    /// <summary>
    /// What the user wants: Running, Stopped, or Removed.
    /// </summary>
    public DesiredState DesiredState { get; set; } = DesiredState.Running;

    /// <summary>
    /// Target replica count for this instance group.
    /// </summary>
    public int DesiredReplicas { get; set; } = 1;

    /// <summary>
    /// The name of the provider that manages this instance (e.g. "Docker", "Containerd", "Kubernetes").
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// When this instance record was first created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this instance record was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Extensible key-value metadata for the instance.
    /// </summary>
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

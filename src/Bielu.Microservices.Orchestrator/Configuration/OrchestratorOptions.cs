namespace Bielu.Microservices.Orchestrator.Configuration;

/// <summary>
/// Configuration options for the microservices orchestrator.
/// </summary>
public class OrchestratorOptions
{
    /// <summary>
    /// The default provider to use when multiple are registered.
    /// </summary>
    public string? DefaultProvider { get; set; }

    /// <summary>
    /// When <c>true</c>, the orchestrator only lists and manages containers that it
    /// created (identified by the <see cref="OrchestratorLabels.ManagedBy"/> label).
    /// When <c>false</c>, all containers on the host are visible.
    /// </summary>
    public bool ManagedContainersOnly { get; set; } = true;

    /// <summary>
    /// Stable identifier of this orchestrator instance, persisted with every
    /// <see cref="Models.ManagedInstance"/> so that, after a restart, the orchestrator
    /// can identify which records it owns. Defaults to a process-stable GUID; for
    /// multi-host deployments this should be configured explicitly (e.g. from machine
    /// identity or configuration) so it survives restarts.
    /// </summary>
    public Guid OrchestratorId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Optional name of a default container network that the orchestrator will
    /// auto-create (if missing) and attach every created container to when the
    /// caller does not specify any networks. This mirrors .NET Aspire's DCP
    /// behavior, which provisions a single shared
    /// <c>aspire-session-network-{id}</c> per session and joins all container
    /// resources to it so they can resolve each other by name.
    ///
    /// When <c>null</c>, a session-scoped name is generated automatically as
    /// <c>bielu-session-network-{OrchestratorId:N}</c>. Set to an empty string
    /// to disable the default-network behavior entirely (containers will then
    /// land on the runtime's default network as before).
    /// </summary>
    public string? DefaultNetworkName { get; set; }

    /// <summary>
    /// When <c>true</c> (default), the orchestrator will, for every created
    /// container, automatically attach it to <see cref="DefaultNetworkName"/>
    /// (creating the network if it does not yet exist) when the caller did not
    /// explicitly request any networks. This matches Aspire/DCP semantics so
    /// containers created through the orchestrator can discover each other via
    /// DNS by their <see cref="Models.CreateContainerRequest.Name"/>.
    /// </summary>
    public bool UseDefaultNetwork { get; set; } = true;

    /// <summary>
    /// Resolves the effective default network name for this orchestrator
    /// instance: <see cref="DefaultNetworkName"/> if set, otherwise a stable
    /// session-scoped name derived from <see cref="OrchestratorId"/>.
    /// Returns <c>null</c> when the user has explicitly disabled the default
    /// network by setting <see cref="DefaultNetworkName"/> to an empty string.
    /// </summary>
    public string? ResolveDefaultNetworkName()
    {
        if (DefaultNetworkName is null)
        {
            return $"bielu-session-network-{OrchestratorId:N}";
        }

        return DefaultNetworkName.Length == 0 ? null : DefaultNetworkName;
    }
}

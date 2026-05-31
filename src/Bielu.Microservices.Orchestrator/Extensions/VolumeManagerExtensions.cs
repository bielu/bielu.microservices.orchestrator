using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Extensions;

/// <summary>
/// Convenience extensions for <see cref="IVolumeManager"/>.
/// </summary>
public static class VolumeManagerExtensions
{
    /// <summary>
    /// Creates a named local-driver volume that is bound to a specific host directory.
    /// Equivalent to:
    /// <c>docker volume create --driver local --opt type=none --opt o=bind --opt device=&lt;hostPath&gt; &lt;name&gt;</c>.
    /// </summary>
    /// <param name="manager">The volume manager.</param>
    /// <param name="name">The volume name.</param>
    /// <param name="hostPath">Absolute path on the host to bind.</param>
    /// <param name="mountOptions">
    /// Mount flags. Defaults to <see cref="LocalMountOptions.Bind"/>.
    /// Combine flags with the bitwise OR operator, e.g.
    /// <c>LocalMountOptions.Bind | LocalMountOptions.ReadOnly</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<VolumeInfo> CreateLocalBoundAsync(
        this IVolumeManager manager,
        string name,
        string hostPath,
        LocalMountOptions mountOptions = LocalMountOptions.Bind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);

        var options = new LocalVolumeOptions
        {
            Device = hostPath,
            MountOptions = mountOptions
        };

        return manager.CreateAsync(name, "local", options.ToDictionary(), cancellationToken);
    }
}

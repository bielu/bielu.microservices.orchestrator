namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents a volume mount that maps a host path (or named volume) into a container path.
/// </summary>
public class VolumeMount
{
    /// <summary>
    /// The path on the host machine, or the name of a named volume.
    /// </summary>
    public string HostPath { get; set; } = string.Empty;

    /// <summary>
    /// The path inside the container where the volume will be mounted.
    /// </summary>
    public string ContainerPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether the mount is read-only. Defaults to <c>false</c>.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// When set, the container manager will auto-create a local-driver bound volume
    /// with these options before starting the container. <see cref="HostPath"/> is
    /// used as the volume name; <see cref="LocalVolumeOptions.Device"/> is the host path.
    /// </summary>
    public LocalVolumeOptions? LocalDriverOptions { get; set; }

    /// <summary>
    /// Parses a Docker-style volume binding string in the form
    /// <c>host:container</c> or <c>host:container:ro|rw</c>.
    /// </summary>
    /// <param name="binding">The binding string to parse.</param>
    /// <returns>The parsed <see cref="VolumeMount"/>.</returns>
    /// <exception cref="ArgumentException">Thrown if the string is null, empty, or malformed.</exception>
    public static VolumeMount Parse(string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            throw new ArgumentException("Volume binding cannot be null or empty.", nameof(binding));
        }

        var parts = binding.Split(':');
        return parts.Length switch
        {
            1 => new VolumeMount { HostPath = parts[0], ContainerPath = parts[0] },
            2 => new VolumeMount { HostPath = parts[0], ContainerPath = parts[1] },
            3 => new VolumeMount
            {
                HostPath = parts[0],
                ContainerPath = parts[1],
                ReadOnly = string.Equals(parts[2], "ro", StringComparison.OrdinalIgnoreCase)
            },
            _ => throw new ArgumentException(
                $"Invalid volume binding '{binding}'. Expected format 'host:container[:ro|rw]'.",
                nameof(binding))
        };
    }

    /// <summary>
    /// Returns <c>true</c> when <see cref="HostPath"/> is a host filesystem path (bind mount)
    /// rather than a named volume. Rooted paths (<c>/data</c>, <c>C:\data</c>) and relative
    /// paths (<c>./data</c>) are treated as bind mounts; plain names (<c>myvolume</c>) are not.
    /// </summary>
    public bool IsBindMount =>
        Path.IsPathRooted(HostPath) ||
        HostPath.StartsWith("./", StringComparison.Ordinal) ||
        HostPath.StartsWith(".\\", StringComparison.Ordinal);

    /// <summary>
    /// Serializes the volume mount into the Docker-style <c>host:container[:ro]</c> format.
    /// </summary>
    public string ToBindString()
    {
        var bind = $"{HostPath}:{ContainerPath}";
        return ReadOnly ? bind + ":ro" : bind;
    }

    /// <inheritdoc />
    public override string ToString() => ToBindString();
}

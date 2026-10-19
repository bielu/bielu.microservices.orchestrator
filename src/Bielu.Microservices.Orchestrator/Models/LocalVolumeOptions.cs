namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Options for creating a local-driver volume bound to a host directory.
/// Produces the driver opts required for
/// <c>docker volume create --driver local --opt type=none --opt o=bind --opt device=…</c>.
/// </summary>
public class LocalVolumeOptions
{
    /// <summary>
    /// The host filesystem path to bind. Maps to the <c>device</c> driver option.
    /// </summary>
    public string Device { get; set; } = string.Empty;

    /// <summary>
    /// The filesystem type passed to <c>mount</c>. Defaults to <c>"none"</c> for a bind mount.
    /// </summary>
    public string Type { get; set; } = "none";

    /// <summary>
    /// Mount flags. Defaults to <see cref="LocalMountOptions.Bind"/>.
    /// Combine flags with the bitwise OR operator, e.g.
    /// <c>LocalMountOptions.Bind | LocalMountOptions.ReadOnly</c>.
    /// </summary>
    public LocalMountOptions MountOptions { get; set; } = LocalMountOptions.Bind;

    /// <summary>
    /// Converts this object to the driver options dictionary accepted by the volume manager.
    /// </summary>
    public IDictionary<string, string> ToDictionary() => new Dictionary<string, string>
    {
        ["type"] = Type,
        ["o"] = FormatMountOptions(MountOptions),
        ["device"] = Device
    };

    private static string FormatMountOptions(LocalMountOptions options)
    {
        var flags = new List<string>();

        if (options.HasFlag(LocalMountOptions.Bind)) flags.Add("bind");
        if (options.HasFlag(LocalMountOptions.ReadOnly)) flags.Add("ro");
        if (options.HasFlag(LocalMountOptions.Suid)) flags.Add("suid");
        if (options.HasFlag(LocalMountOptions.NoExec)) flags.Add("noexec");
        if (options.HasFlag(LocalMountOptions.NoAtime)) flags.Add("noatime");

        return flags.Count > 0 ? string.Join(",", flags) : "bind";
    }
}

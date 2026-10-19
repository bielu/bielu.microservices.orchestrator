namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Mount options for a local-driver bound volume, corresponding to the <c>-o</c> flags passed to <c>mount(8)</c>.
/// Multiple flags can be combined with the bitwise OR operator.
/// </summary>
[Flags]
public enum LocalMountOptions
{
    /// <summary>Bind-mount the device path into the volume.</summary>
    Bind = 1 << 0,

    /// <summary>Mount read-only.</summary>
    ReadOnly = 1 << 1,

    /// <summary>Allow set-user-ID and set-group-ID bits to take effect (overrides <c>nosuid</c>).</summary>
    Suid = 1 << 2,

    /// <summary>Do not allow execution of any binaries on the mounted filesystem.</summary>
    NoExec = 1 << 3,

    /// <summary>Do not update access times when files are read.</summary>
    NoAtime = 1 << 4,
}

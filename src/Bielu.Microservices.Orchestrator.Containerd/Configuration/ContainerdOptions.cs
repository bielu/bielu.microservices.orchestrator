namespace Bielu.Microservices.Orchestrator.Containerd.Configuration;

/// <summary>
/// Configuration options for the containerd runtime provider.
/// </summary>
public class ContainerdOptions
{
    /// <summary>
    /// The containerd gRPC socket endpoint.
    /// </summary>
    public string Endpoint { get; set; } = "unix:///run/containerd/containerd.sock";

    /// <summary>
    /// The default namespace for containerd operations.
    /// </summary>
    public string Namespace { get; set; } = "default";

    /// <summary>
    /// Connection timeout.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Path to the directory containing CNI network configuration files (*.conf / *.conflist).
    /// <para>
    /// <b>Prerequisite:</b> CNI plugins must be installed on the host. The standard location for
    /// CNI plugin binaries is <c>/opt/cni/bin</c>. Install them via your distribution's package
    /// manager (e.g. <c>apt install containernetworking-plugins</c>) or download from
    /// <see href="https://github.com/containernetworking/plugins/releases"/>.
    /// </para>
    /// </summary>
    public string CniConfigPath { get; set; } = "/etc/cni/net.d";

    /// <summary>
    /// Path to the directory containing CNI plugin binaries.
    /// <para>
    /// Used when generating new CNI configuration files so the correct plugin binary paths are
    /// recorded. CNI plugin binaries must already be present at this path on the host.
    /// </para>
    /// </summary>
    public string CniBinPath { get; set; } = "/opt/cni/bin";

    /// <summary>
    /// Default subnet used when creating new CNI bridge networks via <c>CreateAsync</c>.
    /// Change this if <c>10.88.0.0/16</c> conflicts with your existing network topology.
    /// </summary>
    public string CniDefaultSubnet { get; set; } = "10.88.0.0/16";
}

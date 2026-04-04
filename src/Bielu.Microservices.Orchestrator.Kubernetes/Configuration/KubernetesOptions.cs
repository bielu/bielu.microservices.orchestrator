namespace Bielu.Microservices.Orchestrator.Kubernetes.Configuration;

/// <summary>
/// Configuration options for the Kubernetes runtime provider.
/// </summary>
public class KubernetesOptions
{
    /// <summary>
    /// The Kubernetes namespace for operations.
    /// </summary>
    public string Namespace { get; set; } = "default";

    /// <summary>
    /// Path to the kubeconfig file. If null, uses in-cluster or default config.
    /// </summary>
    public string? KubeConfigPath { get; set; }

    /// <summary>
    /// The Kubernetes API server URL. If null, uses config from kubeconfig.
    /// </summary>
    public string? ApiServerUrl { get; set; }

    /// <summary>
    /// Whether to use in-cluster configuration.
    /// </summary>
    public bool UseInClusterConfig { get; set; }
}

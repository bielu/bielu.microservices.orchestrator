using System.Diagnostics;
using System.Reflection;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry;

/// <summary>
/// Provides the <see cref="ActivitySource"/> used for tracing orchestrator operations.
/// </summary>
public static class OrchestratorActivitySource
{
    private static readonly AssemblyName AssemblyName =
        typeof(OrchestratorActivitySource).Assembly.GetName();

    /// <summary>
    /// The name of the activity source.
    /// </summary>
    public static readonly string Name = AssemblyName.Name!;

    /// <summary>
    /// The version of the activity source.
    /// </summary>
    public static readonly string Version = AssemblyName.Version!.ToString();

    /// <summary>
    /// The <see cref="ActivitySource"/> for all orchestrator operations.
    /// </summary>
    public static readonly ActivitySource Source = new(Name, Version);

    // Container operations
    internal const string ContainerList = "container.list";
    internal const string ContainerGet = "container.get";
    internal const string ContainerCreate = "container.create";
    internal const string ContainerStart = "container.start";
    internal const string ContainerStop = "container.stop";
    internal const string ContainerRemove = "container.remove";
    internal const string ContainerGetLogs = "container.get_logs";

    // Image operations
    internal const string ImageList = "image.list";
    internal const string ImageGet = "image.get";
    internal const string ImagePull = "image.pull";
    internal const string ImageRemove = "image.remove";
    internal const string ImageTag = "image.tag";

    // Network operations
    internal const string NetworkList = "network.list";
    internal const string NetworkCreate = "network.create";
    internal const string NetworkRemove = "network.remove";
    internal const string NetworkConnect = "network.connect";
    internal const string NetworkDisconnect = "network.disconnect";

    // Volume operations
    internal const string VolumeList = "volume.list";
    internal const string VolumeCreate = "volume.create";
    internal const string VolumeRemove = "volume.remove";

    // Attribute keys
    internal const string AttributeContainerId = "container.id";
    internal const string AttributeContainerImage = "container.image";
    internal const string AttributeNetworkId = "network.id";
    internal const string AttributeNetworkDriver = "network.driver";
    internal const string AttributeVolumeName = "volume.name";
    internal const string AttributeVolumeDriver = "volume.driver";
    internal const string AttributeImageId = "image.id";
    internal const string AttributeImageTag = "image.tag";
}

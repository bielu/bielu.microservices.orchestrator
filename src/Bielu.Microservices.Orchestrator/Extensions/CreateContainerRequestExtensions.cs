using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Extensions;

/// <summary>
/// Fluent API extension methods for building a <see cref="CreateContainerRequest"/>.
/// </summary>
public static class CreateContainerRequestExtensions
{
    /// <summary>
    /// Sets the container name.
    /// </summary>
    public static CreateContainerRequest WithName(this CreateContainerRequest request, string name)
    {
        request.Name = name;
        return request;
    }

    /// <summary>
    /// Sets the container image.
    /// </summary>
    public static CreateContainerRequest WithImage(this CreateContainerRequest request, string image)
    {
        request.Image = image;
        return request;
    }

    /// <summary>
    /// Sets the command to run in the container.
    /// </summary>
    public static CreateContainerRequest WithCommand(this CreateContainerRequest request, params string[] command)
    {
        request.Command = command.ToList();
        return request;
    }

    /// <summary>
    /// Adds an environment variable to the container.
    /// </summary>
    public static CreateContainerRequest WithEnvironmentVariable(
        this CreateContainerRequest request, string key, string value)
    {
        request.EnvironmentVariables[key] = value;
        return request;
    }

    /// <summary>
    /// Adds a port mapping to the container.
    /// </summary>
    /// <remarks>
    /// The default <paramref name="hostIp"/> of <c>"0.0.0.0"</c> binds the port on all
    /// network interfaces. Use <c>"127.0.0.1"</c> to restrict access to the local machine.
    /// </remarks>
    public static CreateContainerRequest WithPort(
        this CreateContainerRequest request,
        int containerPort,
        int hostPort,
        string protocol = "tcp",
        string hostIp = "0.0.0.0")
    {
        request.Ports.Add(new PortMapping
        {
            ContainerPort = containerPort,
            HostPort = hostPort,
            Protocol = protocol,
            HostIp = hostIp
        });
        return request;
    }

    /// <summary>
    /// Adds a label to the container.
    /// </summary>
    public static CreateContainerRequest WithLabel(
        this CreateContainerRequest request, string key, string value)
    {
        request.Labels[key] = value;
        return request;
    }

    /// <summary>
    /// Adds a volume binding to the container using the Docker-style
    /// <c>host:container[:ro|rw]</c> format.
    /// </summary>
    public static CreateContainerRequest WithVolume(this CreateContainerRequest request, string volume)
    {
        request.Volumes.Add(VolumeMount.Parse(volume));
        return request;
    }

    /// <summary>
    /// Adds a volume mapping from a host path (or named volume) to a container path.
    /// </summary>
    /// <param name="request">The container request.</param>
    /// <param name="hostPath">The host path or named volume.</param>
    /// <param name="containerPath">The path inside the container.</param>
    /// <param name="readOnly">Whether the mount should be read-only.</param>
    public static CreateContainerRequest WithVolume(
        this CreateContainerRequest request,
        string hostPath,
        string containerPath,
        bool readOnly = false)
    {
        request.Volumes.Add(new VolumeMount
        {
            HostPath = hostPath,
            ContainerPath = containerPath,
            ReadOnly = readOnly
        });
        return request;
    }

    /// <summary>
    /// Adds a structured volume mount to the container.
    /// </summary>
    public static CreateContainerRequest WithVolume(this CreateContainerRequest request, VolumeMount volume)
    {
        ArgumentNullException.ThrowIfNull(volume);
        request.Volumes.Add(volume);
        return request;
    }

    /// <summary>
    /// Adds a local-driver bound volume to the container. The container manager will
    /// auto-create the named volume backed by <paramref name="hostPath"/> before
    /// starting the container — no separate volume-creation step is needed.
    /// </summary>
    /// <param name="request">The container request.</param>
    /// <param name="volumeName">The Docker volume name to create or reuse.</param>
    /// <param name="hostPath">Absolute path on the host to bind into the volume.</param>
    /// <param name="containerPath">The path inside the container where the volume will be mounted.</param>
    /// <param name="mountOptions">
    /// Mount flags. Defaults to <see cref="LocalMountOptions.Bind"/>.
    /// Combine flags with the bitwise OR operator, e.g.
    /// <c>LocalMountOptions.Bind | LocalMountOptions.ReadOnly</c>.
    /// </param>
    public static CreateContainerRequest WithLocalBoundVolume(
        this CreateContainerRequest request,
        string volumeName,
        string hostPath,
        string containerPath,
        LocalMountOptions mountOptions = LocalMountOptions.Bind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);

        request.Volumes.Add(new VolumeMount
        {
            HostPath = volumeName,
            ContainerPath = containerPath,
            ReadOnly = mountOptions.HasFlag(LocalMountOptions.ReadOnly),
            LocalDriverOptions = new LocalVolumeOptions
            {
                Device = hostPath,
                MountOptions = mountOptions
            }
        });
        return request;
    }

    /// <summary>
    /// Enables or disables automatic removal of the container when it stops.
    /// </summary>
    public static CreateContainerRequest WithAutoRemove(
        this CreateContainerRequest request, bool autoRemove = true)
    {
        request.AutoRemove = autoRemove;
        return request;
    }

    /// <summary>
    /// Sets the number of container replicas to create.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="replicas"/> is less than 1.</exception>
    public static CreateContainerRequest WithReplicas(this CreateContainerRequest request, int replicas)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(replicas, 1);
        request.Replicas = replicas;
        return request;
    }
}

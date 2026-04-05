using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the image manager using gRPC.
/// </summary>
public class ContainerdImageManager(
    ContainerdOptions options,
    ILogger<ContainerdImageManager> logger) : IImageManager
{
    public Task<IReadOnlyList<ImageInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing containerd images in namespace {Namespace}", LogSanitizer.Sanitize(options.Namespace));
        return Task.FromResult<IReadOnlyList<ImageInfo>>(new List<ImageInfo>().AsReadOnly());
    }

    public Task<ImageInfo?> GetAsync(string imageId, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting containerd image {ImageId} in namespace {Namespace}", LogSanitizer.Sanitize(imageId), LogSanitizer.Sanitize(options.Namespace));
        return Task.FromResult<ImageInfo?>(null);
    }

    public Task PullAsync(PullImageRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Pulling image {Image}:{Tag} via containerd in namespace {Namespace}", LogSanitizer.Sanitize(request.Image), LogSanitizer.Sanitize(request.Tag), LogSanitizer.Sanitize(options.Namespace));
        throw new NotImplementedException("containerd image pull requires generated gRPC stubs from containerd proto definitions.");
    }

    public Task RemoveAsync(string imageId, bool force = false, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Removing containerd image {ImageId}", LogSanitizer.Sanitize(imageId));
        throw new NotImplementedException("containerd image removal requires generated gRPC stubs from containerd proto definitions.");
    }

    public Task TagAsync(string imageId, string repository, string tag, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Tagging containerd image {ImageId} as {Repository}:{Tag}", LogSanitizer.Sanitize(imageId), LogSanitizer.Sanitize(repository), LogSanitizer.Sanitize(tag));
        throw new NotImplementedException("containerd image tagging requires generated gRPC stubs from containerd proto definitions.");
    }
}

using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using k8s;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Kubernetes;

/// <summary>
/// Kubernetes implementation of the image manager.
/// Kubernetes doesn't manage images directly; pulls happen at pod creation time.
/// </summary>
public class KubernetesImageManager(
    IKubernetes client,
    ILogger<KubernetesImageManager> logger) : IImageManager
{

    public async Task<IReadOnlyList<ImageInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        // List images available across nodes
        var nodes = await client.CoreV1.ListNodeAsync(cancellationToken: cancellationToken);
        var images = new List<ImageInfo>();

        foreach (var node in nodes.Items)
        {
            if (node.Status?.Images == null) continue;
            foreach (var image in node.Status.Images)
            {
                images.Add(new ImageInfo
                {
                    Id = image.Names?.FirstOrDefault() ?? string.Empty,
                    Tags = image.Names?.ToList() ?? new List<string>(),
                    Size = image.SizeBytes ?? 0
                });
            }
        }

        return images.AsReadOnly();
    }

    public Task<ImageInfo?> GetAsync(string imageId, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Kubernetes does not support direct image inspection. Image: {ImageId}", LogSanitizer.Sanitize(imageId));
        return Task.FromResult<ImageInfo?>(null);
    }

    public Task PullAsync(PullImageRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Kubernetes pulls images at pod creation time. Image: {Image}:{Tag}", LogSanitizer.Sanitize(request.Image), LogSanitizer.Sanitize(request.Tag));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string imageId, bool force = false, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Kubernetes does not support direct image removal. Image: {ImageId}", LogSanitizer.Sanitize(imageId));
        throw new NotSupportedException("Kubernetes does not support direct image removal from the cluster.");
    }

    public Task TagAsync(string imageId, string repository, string tag, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Kubernetes does not support direct image tagging. Image: {ImageId}", LogSanitizer.Sanitize(imageId));
        throw new NotSupportedException("Kubernetes does not support direct image tagging.");
    }
}

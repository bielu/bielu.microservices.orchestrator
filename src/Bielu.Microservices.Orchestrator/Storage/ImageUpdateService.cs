using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Storage;

/// <summary>
/// Default <see cref="IImageUpdateService"/> implementation.
///
/// Strategy: each managed instance persists its <see cref="ManagedInstance.OriginalRequest"/>
/// in <see cref="IInstanceStore"/>. The container is stamped at creation time with the
/// image digest it was created from (label <see cref="OrchestratorLabels.ImageDigest"/>).
/// We compare that recorded digest with the digest of the image currently available locally
/// (optionally after pulling) and recreate the containers when they differ.
/// </summary>
public class ImageUpdateService(
    IContainerOrchestrator orchestrator,
    IInstanceStore instanceStore,
    ILogger<ImageUpdateService> logger) : IImageUpdateService
{
    public async Task<ImageUpdateStatus> CheckAsync(
        string instanceId,
        ImageUpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var instance = await instanceStore.GetAsync(instanceId, cancellationToken)
                       ?? throw new InvalidOperationException(
                           $"Managed instance '{instanceId}' was not found.");

        return await CheckInternalAsync(instance, options ?? new ImageUpdateOptions(), cancellationToken);
    }

    public async Task<IReadOnlyList<ImageUpdateStatus>> CheckAllAsync(
        ImageUpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new ImageUpdateOptions();
        var instances = await instanceStore.GetAllAsync(cancellationToken);

        var results = new List<ImageUpdateStatus>(instances.Count);
        foreach (var instance in instances)
        {
            try
            {
                results.Add(await CheckInternalAsync(instance, opts, cancellationToken));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Image update check failed for instance {InstanceId}; skipping.",
                    instance.Id);
            }
        }

        return results;
    }

    public async Task<ImageUpdateResult> UpdateAsync(
        string instanceId,
        ImageUpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new ImageUpdateOptions();
        var instance = await instanceStore.GetAsync(instanceId, cancellationToken)
                       ?? throw new InvalidOperationException(
                           $"Managed instance '{instanceId}' was not found.");

        var image = instance.OriginalRequest.Image;
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new InvalidOperationException(
                $"Managed instance '{instanceId}' has no image set on its OriginalRequest.");
        }

        if (opts.Pull)
        {
            await PullAsync(image, cancellationToken);
        }

        var currentDigest = await GetCurrentDigestAsync(instance, cancellationToken);
        var latestDigest = await GetLocalImageDigestAsync(image, cancellationToken);

        var changed = !string.IsNullOrEmpty(latestDigest)
                      && !string.Equals(latestDigest, currentDigest, StringComparison.Ordinal);

        if (!changed && !opts.Force)
        {
            logger.LogInformation(
                "No image update for instance {InstanceId} (digest {Digest}); skipping recreation.",
                instanceId, currentDigest ?? "<none>");

            return new ImageUpdateResult
            {
                Updated = false,
                InstanceId = instanceId,
                PreviousDigest = currentDigest,
                NewDigest = latestDigest ?? currentDigest,
                ContainerIds = instance.ContainerIds.ToList()
            };
        }

        logger.LogInformation(
            "Updating instance {InstanceId}: {Old} -> {New} (force={Force}).",
            instanceId, currentDigest ?? "<none>", latestDigest ?? "<unknown>", opts.Force);

        // Tear down existing containers.
        foreach (var containerId in instance.ContainerIds.ToList())
        {
            try
            {
                await orchestrator.Containers.RemoveAsync(containerId, force: true, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to remove container {ContainerId} during image update; continuing.",
                    containerId);
            }
        }

        // Recreate from the persisted request, refreshing labels with new tracking values.
        var request = instance.OriginalRequest;
        request.Labels[OrchestratorLabels.Image] = image;
        if (!string.IsNullOrEmpty(latestDigest))
        {
            request.Labels[OrchestratorLabels.ImageDigest] = latestDigest!;
        }
        request.Labels[OrchestratorLabels.InstanceId] = instanceId;

        var newId = await orchestrator.Containers.CreateAsync(request, cancellationToken);
        await orchestrator.Containers.StartAsync(newId, cancellationToken);

        var newIds = new List<string> { newId };
        await instanceStore.UpdateContainerIdsAsync(instanceId, newIds, cancellationToken);

        return new ImageUpdateResult
        {
            Updated = true,
            InstanceId = instanceId,
            PreviousDigest = currentDigest,
            NewDigest = latestDigest,
            ContainerIds = newIds
        };
    }

    private async Task<ImageUpdateStatus> CheckInternalAsync(
        ManagedInstance instance,
        ImageUpdateOptions options,
        CancellationToken cancellationToken)
    {
        var image = instance.OriginalRequest.Image;

        if (options.Pull && !string.IsNullOrWhiteSpace(image))
        {
            await PullAsync(image, cancellationToken);
        }

        var current = await GetCurrentDigestAsync(instance, cancellationToken);
        var latest = string.IsNullOrWhiteSpace(image)
            ? null
            : await GetLocalImageDigestAsync(image, cancellationToken);

        return new ImageUpdateStatus
        {
            InstanceId = instance.Id,
            Image = image,
            CurrentDigest = current,
            LatestDigest = latest,
            UpdateAvailable = !string.IsNullOrEmpty(latest)
                              && !string.Equals(latest, current, StringComparison.Ordinal)
        };
    }

    private async Task<string?> GetCurrentDigestAsync(
        ManagedInstance instance,
        CancellationToken cancellationToken)
    {
        // Prefer the label that was stamped when the container was created.
        foreach (var id in instance.ContainerIds)
        {
            var info = await orchestrator.Containers.GetAsync(id, cancellationToken);
            if (info != null
                && info.Labels.TryGetValue(OrchestratorLabels.ImageDigest, out var digest)
                && !string.IsNullOrEmpty(digest))
            {
                return digest;
            }
        }

        return null;
    }

    private async Task<string?> GetLocalImageDigestAsync(
        string image,
        CancellationToken cancellationToken)
    {
        var images = await orchestrator.Images.ListAsync(cancellationToken);
        var match = images.FirstOrDefault(i => i.Tags.Any(t =>
            string.Equals(t, image, StringComparison.OrdinalIgnoreCase)));

        return match?.Id;
    }

    private async Task PullAsync(string image, CancellationToken cancellationToken)
    {
        var (name, tag) = SplitImageReference(image);
        try
        {
            await orchestrator.Images.PullAsync(
                new PullImageRequest { Image = name, Tag = tag },
                cancellationToken);
        }
        catch (Exception ex)
        {
            // A failed pull (network down, registry auth, etc.) should not fail the check.
            // The caller can still see the locally available digest.
            logger.LogWarning(ex, "Failed to pull image {Image}:{Tag}; using local image only.", name, tag);
        }
    }

    private static (string Name, string Tag) SplitImageReference(string image)
    {
        // Split "repo/name:tag" while leaving registry ports (host:5000/repo) intact.
        var lastSlash = image.LastIndexOf('/');
        var lastColon = image.LastIndexOf(':');
        if (lastColon > lastSlash && lastColon < image.Length - 1)
        {
            return (image[..lastColon], image[(lastColon + 1)..]);
        }
        return (image, "latest");
    }
}

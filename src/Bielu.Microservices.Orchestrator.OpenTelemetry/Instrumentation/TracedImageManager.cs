using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Decorator for <see cref="IImageManager"/> that adds OpenTelemetry tracing to all operations.
/// </summary>
public class TracedImageManager(IImageManager inner) : IImageManager
{

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImageInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ImageList);

        var result = await inner.ListAsync(cancellationToken);
        activity?.SetTag("image.list.count", result.Count);
        return result;
    }

    /// <inheritdoc />
    public async Task<ImageInfo?> GetAsync(string imageId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ImageGet);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageId, imageId);

        return await inner.GetAsync(imageId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task PullAsync(PullImageRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ImagePull);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageId, request.Image);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageTag, request.Tag);

        await inner.PullAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string imageId, bool force = false, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ImageRemove);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageId, imageId);
        activity?.SetTag("image.remove.force", force);

        await inner.RemoveAsync(imageId, force, cancellationToken);
    }

    /// <inheritdoc />
    public async Task TagAsync(string imageId, string repository, string tag, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ImageTag);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageId, imageId);
        activity?.SetTag("image.tag.repository", repository);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageTag, tag);

        await inner.TagAsync(imageId, repository, tag, cancellationToken);
    }
}

using System.Diagnostics;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Decorator for <see cref="IImageManager"/> that adds OpenTelemetry tracing and metrics to all operations.
/// </summary>
public class TracedImageManager(IImageManager inner) : IImageManager
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ImageInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ImageList);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.ListAsync(cancellationToken);
            activity?.SetTag("image.list.count", result.Count);
            RecordSuccess(OrchestratorActivitySource.ImageList, startTimestamp);
            return result;
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ImageList, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ImageInfo?> GetAsync(string imageId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ImageGet);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageId, imageId);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.GetAsync(imageId, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.ImageGet, startTimestamp);
            return result;
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ImageGet, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PullAsync(PullImageRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ImagePull);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageId, request.Image);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageTag, request.Tag);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.PullAsync(request, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.ImagePull, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ImagePull, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string imageId, bool force = false, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ImageRemove);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageId, imageId);
        activity?.SetTag("image.remove.force", force);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.RemoveAsync(imageId, force, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.ImageRemove, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ImageRemove, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task TagAsync(string imageId, string repository, string tag, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ImageTag);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageId, imageId);
        activity?.SetTag("image.tag.repository", repository);
        activity?.SetTag(OrchestratorActivitySource.AttributeImageTag, tag);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.TagAsync(imageId, repository, tag, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.ImageTag, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ImageTag, startTimestamp, activity, ex);
            throw;
        }
    }

    private static void RecordSuccess(string operation, long startTimestamp)
    {
        var duration = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var tags = new TagList { { "operation", operation }, { "status", "success" } };
        OrchestratorMetrics.ImageOperationCount.Add(1, tags);
        OrchestratorMetrics.ImageOperationDuration.Record(duration, tags);
    }

    private static void RecordError(string operation, long startTimestamp, Activity? activity, Exception ex)
    {
        var duration = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var tags = new TagList { { "operation", operation }, { "status", "error" } };
        OrchestratorMetrics.ImageOperationCount.Add(1, tags);
        OrchestratorMetrics.ImageOperationDuration.Record(duration, tags);
        OrchestratorMetrics.OperationErrorCount.Add(1, new TagList { { "operation", operation } });
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    }
}

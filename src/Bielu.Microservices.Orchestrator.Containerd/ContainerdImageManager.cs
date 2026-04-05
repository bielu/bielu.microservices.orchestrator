using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Containerd.Services.Images.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Containerd;

/// <summary>
/// containerd implementation of the image manager using gRPC.
/// </summary>
public class ContainerdImageManager(
    Images.ImagesClient imagesClient,
    ContainerdOptions options,
    ILogger<ContainerdImageManager> logger) : IImageManager
{
    public async Task<IReadOnlyList<ImageInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing containerd images in namespace {Namespace}", LogSanitizer.Sanitize(options.Namespace));

        var response = await imagesClient.ListAsync(
            new ListImagesRequest(), NamespaceHeader(), cancellationToken: cancellationToken);

        return response.Images.Select(MapImage).ToList().AsReadOnly();
    }

    public async Task<ImageInfo?> GetAsync(string imageId, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting containerd image {ImageId} in namespace {Namespace}",
            LogSanitizer.Sanitize(imageId), LogSanitizer.Sanitize(options.Namespace));

        try
        {
            var response = await imagesClient.GetAsync(
                new GetImageRequest { Name = imageId }, NamespaceHeader(), cancellationToken: cancellationToken);

            return MapImage(response.Image);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public Task PullAsync(PullImageRequest request, CancellationToken cancellationToken = default)
    {
        // The containerd Images gRPC service only manages image metadata records.
        // Pulling image content requires the Transfer API or running `ctr images pull` on the host.
        throw new NotSupportedException(
            "containerd image pull is not available via the Images gRPC service. " +
            "Use 'ctr images pull' on the host or configure an image transfer agent.");
    }

    public async Task RemoveAsync(string imageId, bool force = false, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Removing containerd image {ImageId}", LogSanitizer.Sanitize(imageId));

        try
        {
            await imagesClient.DeleteAsync(
                new DeleteImageRequest { Name = imageId }, NamespaceHeader(), cancellationToken: cancellationToken);

            logger.LogInformation("Removed containerd image {ImageId}", LogSanitizer.Sanitize(imageId));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            if (!force)
            {
                throw;
            }
            logger.LogDebug("Image {ImageId} not found during removal (force=true, ignoring)", LogSanitizer.Sanitize(imageId));
        }
    }

    public async Task TagAsync(string imageId, string repository, string tag, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Tagging containerd image {ImageId} as {Repository}:{Tag}",
            LogSanitizer.Sanitize(imageId), LogSanitizer.Sanitize(repository), LogSanitizer.Sanitize(tag));

        // Retrieve the source image to copy its target descriptor
        var sourceResponse = await imagesClient.GetAsync(
            new GetImageRequest { Name = imageId }, NamespaceHeader(), cancellationToken: cancellationToken);

        var newName = $"{repository}:{tag}";
        var newImage = new Image
        {
            Name = newName,
            Target = sourceResponse.Image.Target
        };

        // Create or overwrite the named reference
        try
        {
            await imagesClient.CreateAsync(
                new CreateImageRequest { Image = newImage }, NamespaceHeader(), cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            await imagesClient.UpdateAsync(
                new UpdateImageRequest
                {
                    Image = newImage,
                    UpdateMask = new FieldMask { Paths = { "target" } }
                },
                NamespaceHeader(), cancellationToken: cancellationToken);
        }

        logger.LogInformation("Tagged containerd image {ImageId} as {NewName}",
            LogSanitizer.Sanitize(imageId), LogSanitizer.Sanitize(newName));
    }

    private static ImageInfo MapImage(Image image)
    {
        // Extract just the tag portion (e.g. "myapp:v1.0" → ["v1.0"], "myapp" → [])
        var colonIndex = image.Name.LastIndexOf(':');
        var tag = colonIndex > 0 ? image.Name[(colonIndex + 1)..] : string.Empty;
        var tags = string.IsNullOrEmpty(tag) ? new List<string>() : new List<string> { tag };

        return new ImageInfo
        {
            Id = image.Name,
            Tags = tags,
            CreatedAt = image.CreatedAt != null
                ? DateTimeOffset.FromUnixTimeSeconds(image.CreatedAt.Seconds)
                : DateTimeOffset.MinValue
        };
    }

    private Metadata NamespaceHeader() =>
        new() { { "containerd-namespace", options.Namespace } };
}

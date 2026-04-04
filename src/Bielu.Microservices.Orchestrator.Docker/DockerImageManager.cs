using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Docker;

/// <summary>
/// Docker implementation of the image manager.
/// </summary>
public class DockerImageManager : IImageManager
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerImageManager> _logger;

    public DockerImageManager(DockerClient client, ILogger<DockerImageManager> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ImageInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var images = await _client.Images.ListImagesAsync(
            new ImagesListParameters { All = true }, cancellationToken);

        return images.Select(i => new ImageInfo
        {
            Id = i.ID,
            Tags = i.RepoTags?.ToList() ?? new List<string>(),
            Size = i.Size,
            CreatedAt = new DateTimeOffset(i.Created),
            Labels = i.Labels != null ? new Dictionary<string, string>(i.Labels) : new Dictionary<string, string>()
        }).ToList().AsReadOnly();
    }

    public async Task<ImageInfo?> GetAsync(string imageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Images.InspectImageAsync(imageId, cancellationToken);
            return new ImageInfo
            {
                Id = response.ID,
                Tags = response.RepoTags?.ToList() ?? new List<string>(),
                Size = response.Size,
                CreatedAt = new DateTimeOffset(response.Created),
                Labels = response.Config?.Labels != null
                    ? new Dictionary<string, string>(response.Config.Labels)
                    : new Dictionary<string, string>()
            };
        }
        catch (DockerImageNotFoundException)
        {
            return null;
        }
    }

    public async Task PullAsync(PullImageRequest request, CancellationToken cancellationToken = default)
    {
        var pullParams = new ImagesCreateParameters
        {
            FromImage = request.Image,
            Tag = request.Tag
        };

        AuthConfig? authConfig = null;
        if (request.Credentials != null)
        {
            authConfig = new AuthConfig
            {
                ServerAddress = request.Credentials.ServerAddress,
                Username = request.Credentials.Username,
                Password = request.Credentials.Password
            };
        }

        _logger.LogInformation("Pulling image {Image}:{Tag}", request.Image, request.Tag);
        await _client.Images.CreateImageAsync(pullParams, authConfig,
            new Progress<JSONMessage>(), cancellationToken);
        _logger.LogInformation("Pulled image {Image}:{Tag}", request.Image, request.Tag);
    }

    public async Task RemoveAsync(string imageId, bool force = false, CancellationToken cancellationToken = default)
    {
        await _client.Images.DeleteImageAsync(imageId,
            new ImageDeleteParameters { Force = force }, cancellationToken);
        _logger.LogInformation("Removed image {ImageId}", imageId);
    }

    public async Task TagAsync(string imageId, string repository, string tag, CancellationToken cancellationToken = default)
    {
        await _client.Images.TagImageAsync(imageId,
            new ImageTagParameters { RepositoryName = repository, Tag = tag }, cancellationToken);
        _logger.LogInformation("Tagged image {ImageId} as {Repository}:{Tag}", imageId, repository, tag);
    }
}

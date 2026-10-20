using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Bielu.Microservices.Orchestrator.Containerd.Extensions;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Containerd.Services.Images.V1;
using Containerd.Services.Snapshots.V1;
using Containerd.Types;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="ContainerdImageManager"/>, <see cref="ContainerdVolumeManager"/> and the
/// <c>AddContainerd</c> registration.
/// </summary>
public class ContainerdImageAndVolumeTests
{
    private const string I = FakeContainerd.Images;
    private const string S = FakeContainerd.Snapshots;

    private readonly FakeContainerd _grpc = new();
    private readonly ContainerdOptions _options = new() { Namespace = "apps" };

    private ContainerdImageManager Images() =>
        new(new global::Containerd.Services.Images.V1.Images.ImagesClient(_grpc), _options, NullLogger<ContainerdImageManager>.Instance);

    private ContainerdVolumeManager Volumes() =>
        new(new Snapshots.SnapshotsClient(_grpc), _options, NullLogger<ContainerdVolumeManager>.Instance);

    private static Image Image(string name, bool withTimestamp = true) => new()
    {
        Name = name,
        Target = new ContentDescriptor { Digest = $"sha256:{name.GetHashCode():x}", MediaType = "application/vnd.oci.image.index.v1+json" },
        CreatedAt = withTimestamp ? Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 7, 8, 9, 10, 11, TimeSpan.Zero)) : null
    };

    // -----------------------------------------------------------------------
    // Images
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_MapsImageNamesToIdsAndTags()
    {
        // Arrange
        var response = new ListImagesResponse();
        response.Images.Add(Image("docker.io/library/nginx:1.25"));
        response.Images.Add(Image("localhost:5000/app", withTimestamp: false));
        _grpc.On<ListImagesRequest, ListImagesResponse>(I, "List", _ => response);

        // Act
        var images = await Images().ListAsync();

        // Assert
        images[0].Id.ShouldBe("docker.io/library/nginx:1.25");
        images[0].Tags.ShouldBe(["1.25"]);
        images[0].CreatedAt.ShouldBe(new DateTimeOffset(2026, 7, 8, 9, 10, 11, TimeSpan.Zero));
        images[1].Tags.ShouldBe(["5000/app"]);
        images[1].CreatedAt.ShouldBe(DateTimeOffset.MinValue);
        _grpc.Calls.ShouldAllBe(c => c.Namespace == "apps");
    }

    [Fact]
    public async Task GetAsync_FoundOrMissing()
    {
        // Arrange
        _grpc.On<GetImageRequest, GetImageResponse>(I, "Get", r => r.Name == "busybox"
            ? new GetImageResponse { Image = Image("busybox") }
            : throw new RpcException(new Status(StatusCode.NotFound, "missing")));

        // Act
        var found = await Images().GetAsync("busybox");
        var missing = await Images().GetAsync("nope");

        // Assert
        found!.Id.ShouldBe("busybox");
        found.Tags.ShouldBeEmpty();
        missing.ShouldBeNull();
    }

    [Fact]
    public async Task PullAsync_IsNotSupported()
    {
        // Act & Assert
        await Should.ThrowAsync<NotSupportedException>(() => Images().PullAsync(new PullImageRequest { Image = "nginx" }));
    }

    [Fact]
    public async Task RemoveAsync_DeletesTheImage()
    {
        // Arrange
        _grpc.On<DeleteImageRequest, Empty>(I, "Delete", _ => new Empty());

        // Act
        await Images().RemoveAsync("nginx:1.25");

        // Assert
        _grpc.Requests<DeleteImageRequest>(I, "Delete").Single().Name.ShouldBe("nginx:1.25");
    }

    [Fact]
    public async Task RemoveAsync_MissingImage_ThrowsUnlessForced()
    {
        // Arrange
        _grpc.Fail(I, "Delete", StatusCode.NotFound);

        // Act & Assert
        (await Should.ThrowAsync<RpcException>(() => Images().RemoveAsync("nope"))).StatusCode.ShouldBe(StatusCode.NotFound);
        await Should.NotThrowAsync(() => Images().RemoveAsync("nope", force: true));
    }

    [Fact]
    public async Task TagAsync_NewName_CreatesAReferenceToTheSourceTarget()
    {
        // Arrange
        var source = Image("nginx:1.25");
        _grpc.On<GetImageRequest, GetImageResponse>(I, "Get", _ => new GetImageResponse { Image = source })
            .On<CreateImageRequest, CreateImageResponse>(I, "Create", r => new CreateImageResponse { Image = r.Image });

        // Act
        await Images().TagAsync("nginx:1.25", "registry.test/nginx", "stable");

        // Assert
        var created = _grpc.Requests<CreateImageRequest>(I, "Create").Single().Image;
        created.Name.ShouldBe("registry.test/nginx:stable");
        created.Target.ShouldBe(source.Target);
        _grpc.Requests<UpdateImageRequest>(I, "Update").ShouldBeEmpty();
    }

    [Fact]
    public async Task TagAsync_ExistingName_UpdatesItsTarget()
    {
        // Arrange
        var source = Image("nginx:1.25");
        _grpc.On<GetImageRequest, GetImageResponse>(I, "Get", _ => new GetImageResponse { Image = source })
            .Fail(I, "Create", StatusCode.AlreadyExists)
            .On<UpdateImageRequest, UpdateImageResponse>(I, "Update", r => new UpdateImageResponse { Image = r.Image });

        // Act
        await Images().TagAsync("nginx:1.25", "nginx", "stable");

        // Assert
        var update = _grpc.Requests<UpdateImageRequest>(I, "Update").Single();
        update.Image.Name.ShouldBe("nginx:stable");
        update.UpdateMask.Paths.ShouldBe(["target"]);
    }

    // -----------------------------------------------------------------------
    // Volumes (snapshots)
    // -----------------------------------------------------------------------

    private static Info Snapshot(string name)
    {
        var info = new Info { Name = name, CreatedAt = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) };
        info.Labels["owner"] = "web";
        return info;
    }

    [Fact]
    public async Task VolumeListAsync_ReadsEveryStreamedPage()
    {
        // Arrange
        var first = new ListSnapshotsResponse();
        first.Info.Add(Snapshot("a"));
        var second = new ListSnapshotsResponse();
        second.Info.Add(Snapshot("b"));
        second.Info.Add(new Info { Name = "c" });
        _grpc.OnStream<ListSnapshotsRequest, ListSnapshotsResponse>(S, "List", _ => [first, second]);

        // Act
        var volumes = await Volumes().ListAsync();

        // Assert
        volumes.Select(v => v.Name).ShouldBe(["a", "b", "c"]);
        volumes[0].Driver.ShouldBe("containerd-snapshotter");
        volumes[0].Labels["owner"].ShouldBe("web");
        volumes[2].CreatedAt.ShouldBe(DateTimeOffset.MinValue);
        _grpc.Calls.Single().Namespace.ShouldBe("apps");
    }

    [Fact]
    public async Task VolumeGetAsync_FoundOrMissing()
    {
        // Arrange
        _grpc.On<StatSnapshotRequest, StatSnapshotResponse>(S, "Stat", r => r.Key == "a"
            ? new StatSnapshotResponse { Info = Snapshot("a") }
            : throw new RpcException(new Status(StatusCode.NotFound, "missing")));

        // Act & Assert
        (await Volumes().GetAsync("a"))!.CreatedAt.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        (await Volumes().GetAsync("b")).ShouldBeNull();
    }

    [Fact]
    public async Task VolumeCreateAsync_IsNotSupported()
    {
        // Act & Assert
        await Should.ThrowAsync<NotSupportedException>(() => Volumes().CreateAsync("data"));
    }

    [Fact]
    public async Task VolumeRemoveAsync_RemovesTheSnapshotAndToleratesMissingOnesWhenForced()
    {
        // Arrange
        _grpc.On<RemoveSnapshotRequest, Empty>(S, "Remove", r => r.Key == "a"
            ? new Empty()
            : throw new RpcException(new Status(StatusCode.NotFound, "missing")));

        // Act
        await Volumes().RemoveAsync("a");
        await Volumes().RemoveAsync("gone", force: true);

        // Assert
        (await Should.ThrowAsync<RpcException>(() => Volumes().RemoveAsync("gone"))).StatusCode.ShouldBe(StatusCode.NotFound);
        _grpc.Requests<RemoveSnapshotRequest>(S, "Remove").Select(r => r.Key).ShouldBe(["a", "gone", "gone"]);
    }

    // -----------------------------------------------------------------------
    // Registration
    // -----------------------------------------------------------------------

    [Fact]
    public void AddContainerd_RegistersTheProviderAndItsOptions()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging();

        // Act
        services.AddMicroservicesOrchestrator(b => b.AddContainerd(o =>
        {
            o.Endpoint = "http://localhost:10010";
            o.Namespace = "apps";
        }));
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<ContainerdOptions>().Namespace.ShouldBe("apps");
        provider.GetRequiredService<IContainerManager>().ShouldBeOfType<ContainerdContainerManager>();
        provider.GetRequiredService<IImageManager>().ShouldBeOfType<ContainerdImageManager>();
        provider.GetRequiredService<INetworkManager>().ShouldBeOfType<ContainerdNetworkManager>();
        provider.GetRequiredService<IVolumeManager>().ShouldBeOfType<ContainerdVolumeManager>();
        provider.GetRequiredService<IContainerOrchestrator>().ShouldBeOfType<ContainerdContainerOrchestrator>();
    }
}

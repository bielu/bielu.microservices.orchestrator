using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Storage;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="ImageUpdateService"/>.
/// </summary>
public class ImageUpdateServiceTests
{
    private const string InstanceId = "web-app";
    private const string Image = "nginx:1.25";
    private const string OldDigest = "sha256:aaaa";
    private const string NewDigest = "sha256:bbbb";

    private readonly IContainerOrchestrator _orchestrator = Substitute.For<IContainerOrchestrator>();
    private readonly IContainerManager _containers = Substitute.For<IContainerManager>();
    private readonly IImageManager _images = Substitute.For<IImageManager>();
    private readonly IInstanceStore _store = new InMemoryInstanceStore();
    private readonly ImageUpdateService _service;

    public ImageUpdateServiceTests()
    {
        _orchestrator.Containers.Returns(_containers);
        _orchestrator.Images.Returns(_images);
        var logger = Substitute.For<ILogger<ImageUpdateService>>();
        _service = new ImageUpdateService(_orchestrator, _store, logger);
    }

    private async Task SeedInstanceAsync(string containerId = "ctr-1")
    {
        await _store.SaveAsync(new ManagedInstance
        {
            Id = InstanceId,
            ContainerIds = [containerId],
            OriginalRequest = new CreateContainerRequest { Name = InstanceId, Image = Image },
            DesiredState = DesiredState.Running,
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private void SetupContainerWithDigest(string containerId, string? digest)
    {
        var info = new ContainerInfo { Id = containerId, Name = InstanceId };
        if (digest != null) info.Labels[OrchestratorLabels.ImageDigest] = digest;
        _containers.GetAsync(containerId, Arg.Any<CancellationToken>()).Returns(info);
    }

    private void SetupLocalImage(string? digest)
    {
        var list = digest == null
            ? new List<ImageInfo>()
            : [new ImageInfo { Id = digest, Tags = { Image } }];
        _images.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ImageInfo>)list);
    }

    [Fact]
    public async Task CheckAsync_ReportsNoUpdate_WhenDigestsMatch()
    {
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(OldDigest);

        var status = await _service.CheckAsync(InstanceId, new ImageUpdateOptions { Pull = false });

        status.InstanceId.ShouldBe(InstanceId);
        status.CurrentDigest.ShouldBe(OldDigest);
        status.LatestDigest.ShouldBe(OldDigest);
        status.UpdateAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckAsync_ReportsUpdateAvailable_WhenLocalDigestChanged()
    {
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(NewDigest);

        var status = await _service.CheckAsync(InstanceId, new ImageUpdateOptions { Pull = false });

        status.UpdateAvailable.ShouldBeTrue();
        status.CurrentDigest.ShouldBe(OldDigest);
        status.LatestDigest.ShouldBe(NewDigest);
    }

    [Fact]
    public async Task CheckAsync_ThrowsWhenInstanceMissing()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => _service.CheckAsync("missing", new ImageUpdateOptions { Pull = false }));
        ex.Message.ShouldContain("missing");
    }

    [Fact]
    public async Task UpdateAsync_DoesNothing_WhenDigestUnchanged()
    {
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(OldDigest);

        var result = await _service.UpdateAsync(InstanceId, new ImageUpdateOptions { Pull = false });

        result.Updated.ShouldBeFalse();
        result.PreviousDigest.ShouldBe(OldDigest);
        await _containers.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _containers.DidNotReceive().CreateAsync(Arg.Any<CreateContainerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RecreatesContainer_WhenDigestChanged()
    {
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(NewDigest);
        _containers.CreateAsync(Arg.Any<CreateContainerRequest>(), Arg.Any<CancellationToken>())
            .Returns("ctr-2");

        var result = await _service.UpdateAsync(InstanceId, new ImageUpdateOptions { Pull = false });

        result.Updated.ShouldBeTrue();
        result.PreviousDigest.ShouldBe(OldDigest);
        result.NewDigest.ShouldBe(NewDigest);
        result.ContainerIds.ShouldContain("ctr-2");

        await _containers.Received(1).RemoveAsync("ctr-1", true, false, Arg.Any<CancellationToken>());
        await _containers.Received(1).CreateAsync(
            Arg.Is<CreateContainerRequest>(r =>
                r.Labels[OrchestratorLabels.ImageDigest] == NewDigest
                && r.Labels[OrchestratorLabels.InstanceId] == InstanceId),
            Arg.Any<CancellationToken>());
        await _containers.Received(1).StartAsync("ctr-2", Arg.Any<CancellationToken>());

        var stored = await _store.GetAsync(InstanceId);
        stored!.ContainerIds.ShouldBe(["ctr-2"]);
    }

    [Fact]
    public async Task UpdateAsync_RecreatesContainer_WhenForceEvenIfDigestUnchanged()
    {
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(OldDigest);
        _containers.CreateAsync(Arg.Any<CreateContainerRequest>(), Arg.Any<CancellationToken>())
            .Returns("ctr-2");

        var result = await _service.UpdateAsync(InstanceId,
            new ImageUpdateOptions { Pull = false, Force = true });

        result.Updated.ShouldBeTrue();
        await _containers.Received(1).RemoveAsync("ctr-1", true, false, Arg.Any<CancellationToken>());
        await _containers.Received(1).StartAsync("ctr-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_ReturnsStatusForEveryInstance()
    {
        await SeedInstanceAsync();
        await _store.SaveAsync(new ManagedInstance
        {
            Id = "other",
            ContainerIds = ["ctr-x"],
            OriginalRequest = new CreateContainerRequest { Name = "other", Image = Image },
            DesiredState = DesiredState.Running,
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });

        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupContainerWithDigest("ctr-x", OldDigest);
        SetupLocalImage(NewDigest);

        var statuses = await _service.CheckAllAsync(new ImageUpdateOptions { Pull = false });

        statuses.Count.ShouldBe(2);
        statuses.ShouldAllBe(s => s.UpdateAvailable);
    }

    private async Task SeedInstanceWithImageAsync(string id, string image, params string[] containerIds)
    {
        await _store.SaveAsync(new ManagedInstance
        {
            Id = id,
            ContainerIds = containerIds,
            OriginalRequest = new CreateContainerRequest { Name = id, Image = image },
            DesiredState = DesiredState.Running,
            ProviderName = "Docker",
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    [Theory]
    [InlineData("nginx:1.25", "nginx", "1.25")]
    [InlineData("nginx", "nginx", "latest")]
    [InlineData("localhost:5000/team/app", "localhost:5000/team/app", "latest")]
    [InlineData("localhost:5000/team/app:2.0", "localhost:5000/team/app", "2.0")]
    [InlineData("nginx:", "nginx:", "latest")]
    public async Task CheckAsync_DefaultOptions_PullsTheImageReferenceSplitIntoNameAndTag(string image, string name, string tag)
    {
        // Arrange
        await SeedInstanceWithImageAsync(InstanceId, image, "ctr-1");
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(OldDigest);

        // Act
        await _service.CheckAsync(InstanceId);

        // Assert
        await _images.Received(1).PullAsync(
            Arg.Is<PullImageRequest>(r => r.Image == name && r.Tag == tag),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAsync_PullFails_StillReportsTheLocalDigest()
    {
        // Arrange
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(NewDigest);
        _images.PullAsync(Arg.Any<PullImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("registry unreachable")));

        // Act
        var status = await _service.CheckAsync(InstanceId, new ImageUpdateOptions { Pull = true });

        // Assert
        status.LatestDigest.ShouldBe(NewDigest);
        status.UpdateAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckAsync_InstanceWithoutImage_ReportsNoLatestDigestAndDoesNotPull()
    {
        // Arrange
        await SeedInstanceWithImageAsync(InstanceId, "", "ctr-1");
        SetupContainerWithDigest("ctr-1", OldDigest);

        // Act
        var status = await _service.CheckAsync(InstanceId, new ImageUpdateOptions { Pull = true });

        // Assert
        status.LatestDigest.ShouldBeNull();
        status.UpdateAvailable.ShouldBeFalse();
        await _images.DidNotReceive().PullAsync(Arg.Any<PullImageRequest>(), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAsync_ContainersWithoutDigestLabel_FallThroughToTheNextContainer()
    {
        // Arrange: the first container is gone, the second has no label, the third carries the digest.
        await SeedInstanceWithImageAsync(InstanceId, Image, "gone", "unlabelled", "labelled");
        _containers.GetAsync("gone", Arg.Any<CancellationToken>()).Returns((ContainerInfo?)null);
        SetupContainerWithDigest("unlabelled", null);
        SetupContainerWithDigest("labelled", OldDigest);
        SetupLocalImage(OldDigest);

        // Act
        var status = await _service.CheckAsync(InstanceId, new ImageUpdateOptions { Pull = false });

        // Assert
        status.CurrentDigest.ShouldBe(OldDigest);
        status.UpdateAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckAsync_ImageNotAvailableLocally_ReportsNoUpdate()
    {
        // Arrange
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(null);

        // Act
        var status = await _service.CheckAsync(InstanceId, new ImageUpdateOptions { Pull = false });

        // Assert
        status.LatestDigest.ShouldBeNull();
        status.UpdateAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckAllAsync_OneInstanceFails_SkipsItAndReturnsTheRest()
    {
        // Arrange
        await SeedInstanceAsync();
        await SeedInstanceWithImageAsync("broken", Image, "ctr-broken");
        SetupContainerWithDigest("ctr-1", OldDigest);
        _containers.GetAsync("ctr-broken", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ContainerInfo?>(new InvalidOperationException("runtime down")));
        SetupLocalImage(NewDigest);

        // Act
        var statuses = await _service.CheckAllAsync();

        // Assert
        statuses.ShouldHaveSingleItem().InstanceId.ShouldBe(InstanceId);
    }

    [Fact]
    public async Task UpdateAsync_InstanceMissing_Throws()
    {
        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => _service.UpdateAsync("missing"));
        ex.Message.ShouldContain("missing");
    }

    [Fact]
    public async Task UpdateAsync_InstanceWithoutImage_Throws()
    {
        // Arrange
        await SeedInstanceWithImageAsync(InstanceId, " ", "ctr-1");

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => _service.UpdateAsync(InstanceId));
        ex.Message.ShouldContain("no image");
    }

    [Fact]
    public async Task UpdateAsync_DefaultOptions_PullsBeforeComparingDigests()
    {
        // Arrange
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(OldDigest);

        // Act
        var result = await _service.UpdateAsync(InstanceId);

        // Assert
        result.Updated.ShouldBeFalse();
        await _images.Received(1).PullAsync(
            Arg.Is<PullImageRequest>(r => r.Image == "nginx" && r.Tag == "1.25"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ImageNotAvailableLocally_KeepsTheCurrentDigest()
    {
        // Arrange
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(null);

        // Act
        var result = await _service.UpdateAsync(InstanceId, new ImageUpdateOptions { Pull = false });

        // Assert
        result.Updated.ShouldBeFalse();
        result.PreviousDigest.ShouldBe(OldDigest);
        result.NewDigest.ShouldBe(OldDigest);
        result.ContainerIds.ShouldBe(["ctr-1"]);
    }

    [Fact]
    public async Task UpdateAsync_RemovalFails_StillRecreatesTheContainer()
    {
        // Arrange
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", OldDigest);
        SetupLocalImage(NewDigest);
        _containers.RemoveAsync("ctr-1", true, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("already gone")));
        _containers.CreateAsync(Arg.Any<CreateContainerRequest>(), Arg.Any<CancellationToken>()).Returns("ctr-new");

        // Act
        var result = await _service.UpdateAsync(InstanceId, new ImageUpdateOptions { Pull = false });

        // Assert
        result.Updated.ShouldBeTrue();
        result.ContainerIds.ShouldBe(["ctr-new"]);
        await _containers.Received(1).StartAsync("ctr-new", Arg.Any<CancellationToken>());
        (await _store.GetAsync(InstanceId))!.ContainerIds.ShouldBe(["ctr-new"]);
    }

    [Fact]
    public async Task UpdateAsync_ForcedWithoutLocalImage_RecreatesWithoutADigestLabel()
    {
        // Arrange
        await SeedInstanceAsync();
        SetupContainerWithDigest("ctr-1", null);
        SetupLocalImage(null);
        _containers.CreateAsync(Arg.Any<CreateContainerRequest>(), Arg.Any<CancellationToken>()).Returns("ctr-new");

        // Act
        var result = await _service.UpdateAsync(InstanceId, new ImageUpdateOptions { Pull = false, Force = true });

        // Assert
        result.Updated.ShouldBeTrue();
        result.NewDigest.ShouldBeNull();
        await _containers.Received(1).CreateAsync(
            Arg.Is<CreateContainerRequest>(r =>
                !r.Labels.ContainsKey(OrchestratorLabels.ImageDigest)
                && r.Labels[OrchestratorLabels.Image] == Image
                && r.Labels[OrchestratorLabels.InstanceId] == InstanceId),
            Arg.Any<CancellationToken>());
    }
}

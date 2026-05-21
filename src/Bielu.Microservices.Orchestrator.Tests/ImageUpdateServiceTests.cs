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
        await _containers.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
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

        await _containers.Received(1).RemoveAsync("ctr-1", true, Arg.Any<CancellationToken>());
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
        await _containers.Received(1).RemoveAsync("ctr-1", true, Arg.Any<CancellationToken>());
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
}

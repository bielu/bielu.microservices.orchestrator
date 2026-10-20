using System.Net;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Kubernetes;
using Bielu.Microservices.Orchestrator.Kubernetes.Configuration;
using Bielu.Microservices.Orchestrator.Kubernetes.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for the Kubernetes provider, run against <see cref="FakeKubernetesApi"/> through the real
/// Kubernetes client.
/// </summary>
public class KubernetesProviderTests
{
    private const string Ns = "apps";
    private const string Pods = "/api/v1/namespaces/apps/pods";
    private const string Pvcs = "/api/v1/namespaces/apps/persistentvolumeclaims";
    private const string Services = "/api/v1/namespaces/apps/services";

    private readonly FakeKubernetesApi _api = new();
    private readonly IKubernetes _client;
    private readonly KubernetesOptions _options = new() { Namespace = Ns };
    private readonly OrchestratorOptions _orchestratorOptions = new();

    public KubernetesProviderTests() => _client = _api.CreateClient();

    private KubernetesContainerManager Containers() =>
        new(_client, _options, _orchestratorOptions, NullLogger<KubernetesContainerManager>.Instance);

    private static V1Pod Pod(string name, string? phase = "Running", IDictionary<string, string>? labels = null, V1PodSpec? spec = null) => new()
    {
        Metadata = new V1ObjectMeta
        {
            Name = name,
            Uid = $"uid-{name}",
            Labels = labels,
            CreationTimestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        },
        Spec = spec ?? new V1PodSpec { Containers = [new V1Container { Name = name, Image = "nginx:1.25" }] },
        Status = phase == null ? null : new V1PodStatus { Phase = phase }
    };

    // -----------------------------------------------------------------------
    // Containers (pods)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_ManagedOnly_FiltersByTheManagedByLabelAndMapsPods()
    {
        // Arrange
        _api.On(HttpMethod.Get, Pods, new V1PodList
        {
            Items = [Pod("web", "Running", new Dictionary<string, string> { ["tier"] = "front" }), Pod("job", "Succeeded")]
        });

        // Act
        var containers = await Containers().ListAsync();

        // Assert
        _api.Single(HttpMethod.Get, Pods).Query.ShouldContain($"labelSelector={OrchestratorLabels.ManagedBy}={OrchestratorLabels.ManagedByValue}");
        containers.Count.ShouldBe(2);
        var web = containers.Single(c => c.Name == "web");
        web.Id.ShouldBe("uid-web");
        web.Image.ShouldBe("nginx:1.25");
        web.State.ShouldBe(ContainerState.Running);
        web.Labels["tier"].ShouldBe("front");
        web.CreatedAt.ShouldBe(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var job = containers.Single(c => c.Name == "job");
        job.State.ShouldBe(ContainerState.Exited);
        job.Labels.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAsync_AllContainers_SendsNoLabelSelector()
    {
        // Arrange
        _orchestratorOptions.ManagedContainersOnly = false;
        _api.On(HttpMethod.Get, Pods, new V1PodList { Items = [] });

        // Act
        await Containers().ListAsync(all: true);

        // Assert
        _api.Single(HttpMethod.Get, Pods).Query.ShouldNotContain("labelSelector");
    }

    [Theory]
    [InlineData("Pending", ContainerState.Created)]
    [InlineData("Running", ContainerState.Running)]
    [InlineData("Succeeded", ContainerState.Exited)]
    [InlineData("Failed", ContainerState.Dead)]
    [InlineData("Unknown", ContainerState.Unknown)]
    [InlineData("Evicted", ContainerState.Unknown)]
    [InlineData(null, ContainerState.Unknown)]
    public async Task GetAsync_PodPhase_MapsToContainerState(string? phase, ContainerState expected)
    {
        // Arrange
        _api.On(HttpMethod.Get, $"{Pods}/web", Pod("web", phase));

        // Act
        var container = await Containers().GetAsync("web");

        // Assert
        container.ShouldNotBeNull();
        container.State.ShouldBe(expected);
    }

    [Fact]
    public async Task GetAsync_PodNotFound_ReturnsNull()
    {
        // Act
        var container = await Containers().GetAsync("missing");

        // Assert
        container.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_ServerError_Throws()
    {
        // Arrange
        _api.On(HttpMethod.Get, $"{Pods}/web", FakeKubernetesApi.Status(500, "InternalError"), HttpStatusCode.InternalServerError);

        // Act & Assert
        await Should.ThrowAsync<HttpOperationException>(() => Containers().GetAsync("web"));
    }

    [Fact]
    public async Task GetAsync_PodWithVolumes_MapsEachMountToItsSource()
    {
        // Arrange
        var spec = new V1PodSpec
        {
            Containers =
            [
                new V1Container
                {
                    Name = "web",
                    Image = "nginx:1.25",
                    VolumeMounts =
                    [
                        new V1VolumeMount { Name = "host", MountPath = "/host", ReadOnlyProperty = true },
                        new V1VolumeMount { Name = "claim", MountPath = "/claim" },
                        new V1VolumeMount { Name = "config", MountPath = "/config" },
                        new V1VolumeMount { Name = "secret", MountPath = "/secret" },
                        new V1VolumeMount { Name = "scratch", MountPath = "/scratch" },
                        new V1VolumeMount { Name = "undeclared", MountPath = "/nowhere" },
                    ]
                },
                new V1Container { Name = "sidecar", Image = "busybox" }
            ],
            Volumes =
            [
                new V1Volume { Name = "host", HostPath = new V1HostPathVolumeSource { Path = "/srv/data" } },
                new V1Volume { Name = "claim", PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource { ClaimName = "pvc-1" } },
                new V1Volume { Name = "config", ConfigMap = new V1ConfigMapVolumeSource { Name = "cm-1" } },
                new V1Volume { Name = "secret", Secret = new V1SecretVolumeSource { SecretName = "s-1" } },
                new V1Volume { Name = "scratch", EmptyDir = new V1EmptyDirVolumeSource() },
            ]
        };
        _api.On(HttpMethod.Get, $"{Pods}/web", Pod("web", spec: spec));

        // Act
        var container = await Containers().GetAsync("web");

        // Assert
        container!.Volumes.Select(v => (v.HostPath, v.ContainerPath, v.ReadOnly)).ShouldBe(
        [
            ("/srv/data", "/host", true),
            ("pvc-1", "/claim", false),
            ("cm-1", "/config", false),
            ("s-1", "/secret", false),
            ("scratch", "/scratch", false),
        ]);
    }

    [Fact]
    public async Task CreateAsync_SingleReplica_PostsAPodBuiltFromTheRequest()
    {
        // Arrange
        _api.On(HttpMethod.Post, Pods, r => (HttpStatusCode.Created, r.BodyAs<V1Pod>()));
        var request = new CreateContainerRequest
        {
            Name = "web",
            Image = "nginx:1.25",
            Command = ["nginx", "-g", "daemon off;"],
            RestartPolicy = RestartPolicy.OnFailure
        }.WithEnvironmentVariable("MODE", "prod").WithPort(80, 8080, "tcp").WithLabel("tier", "front");

        // Act
        var name = await Containers().CreateAsync(request);

        // Assert
        name.ShouldBe("web");
        var pod = _api.Single(HttpMethod.Post, Pods).BodyAs<V1Pod>();
        pod.Metadata.NamespaceProperty.ShouldBe(Ns);
        pod.Metadata.Labels[OrchestratorLabels.ManagedBy].ShouldBe(OrchestratorLabels.ManagedByValue);
        pod.Metadata.Labels["tier"].ShouldBe("front");
        var container = pod.Spec.Containers.ShouldHaveSingleItem();
        container.Name.ShouldBe("web");
        container.Image.ShouldBe("nginx:1.25");
        container.Command.ShouldBe(["nginx", "-g", "daemon off;"]);
        container.Env.ShouldHaveSingleItem().Value.ShouldBe("prod");
        var port = container.Ports.ShouldHaveSingleItem();
        port.ContainerPort.ShouldBe(80);
        port.Protocol.ShouldBe("TCP");
        pod.Spec.RestartPolicy.ShouldBe("OnFailure");
    }

    [Theory]
    [InlineData(RestartPolicy.Always, "Always")]
    [InlineData(RestartPolicy.UnlessStopped, "Always")]
    [InlineData(RestartPolicy.No, "Never")]
    public async Task CreateAsync_RestartPolicy_MapsToThePodPolicy(RestartPolicy policy, string expected)
    {
        // Arrange
        _api.On(HttpMethod.Post, Pods, r => (HttpStatusCode.Created, r.BodyAs<V1Pod>()));

        // Act
        await Containers().CreateAsync(new CreateContainerRequest { Name = "web", Image = "nginx", RestartPolicy = policy });

        // Assert
        _api.Single(HttpMethod.Post, Pods).BodyAs<V1Pod>().Spec.RestartPolicy.ShouldBe(expected);
    }

    [Fact]
    public async Task CreateAsync_NoName_GeneratesOne()
    {
        // Arrange
        _api.On(HttpMethod.Post, Pods, r => (HttpStatusCode.Created, r.BodyAs<V1Pod>()));

        // Act
        var name = await Containers().CreateAsync(new CreateContainerRequest { Image = "nginx" });

        // Assert
        name.ShouldStartWith("orchestrator-");
        _api.Single(HttpMethod.Post, Pods).BodyAs<V1Pod>().Spec.Containers.Single().Name.ShouldBe("main");
    }

    [Fact]
    public async Task CreateAsync_MultipleReplicas_CreatesIndexedPodsWithGroupLabels()
    {
        // Arrange
        _api.On(HttpMethod.Post, Pods, r => (HttpStatusCode.Created, r.BodyAs<V1Pod>()));

        // Act
        var first = await Containers().CreateAsync(new CreateContainerRequest { Name = "web", Image = "nginx", Replicas = 3 });

        // Assert
        first.ShouldBe("web-0");
        var pods = _api.Requests.Where(r => r.Method == HttpMethod.Post).Select(r => r.BodyAs<V1Pod>()).ToList();
        pods.Select(p => p.Metadata.Name).ShouldBe(["web-0", "web-1", "web-2"]);
        pods.ShouldAllBe(p => p.Metadata.Labels[OrchestratorLabels.Group] == "web");
        pods.Select(p => p.Metadata.Labels[OrchestratorLabels.ReplicaIndex]).ShouldBe(["0", "1", "2"]);
    }

    [Fact]
    public async Task CreateAsync_MultipleReplicasWithoutName_UsesAGeneratedGroup()
    {
        // Arrange
        _api.On(HttpMethod.Post, Pods, r => (HttpStatusCode.Created, r.BodyAs<V1Pod>()));

        // Act
        var first = await Containers().CreateAsync(new CreateContainerRequest { Image = "nginx", Replicas = 2 });

        // Assert
        first.ShouldStartWith("orchestrator-");
        first.ShouldEndWith("-0");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_NoReplicas_Throws(int replicas)
    {
        // Act & Assert
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => Containers().CreateAsync(new CreateContainerRequest { Image = "nginx", Replicas = replicas }));
        _api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartAsync_IsANoOp()
    {
        // Act
        await Containers().StartAsync("web");

        // Assert
        _api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScaleAsync_IsNotSupported()
    {
        // Act & Assert
        await Should.ThrowAsync<NotSupportedException>(() => Containers().ScaleAsync("web", 3));
    }

    [Fact]
    public async Task StopAsync_DeletesThePodWithTheTimeoutAsGracePeriod()
    {
        // Arrange
        _api.On(HttpMethod.Delete, $"{Pods}/web", Pod("web"));

        // Act
        await Containers().StopAsync("web", TimeSpan.FromSeconds(12));
        await Containers().StopAsync("web");

        // Assert
        var deletes = _api.Requests.Where(r => r.Method == HttpMethod.Delete).ToList();
        deletes[0].Query.ShouldContain("gracePeriodSeconds=12");
        deletes[1].Query.ShouldNotContain("gracePeriodSeconds");
    }

    [Fact]
    public async Task RemoveAsync_Force_DeletesImmediatelyWithoutReadingVolumes()
    {
        // Arrange
        _api.On(HttpMethod.Delete, $"{Pods}/web", Pod("web"));

        // Act
        await Containers().RemoveAsync("web", force: true);

        // Assert
        _api.Requests.ShouldHaveSingleItem().Query.ShouldContain("gracePeriodSeconds=0");
    }

    [Fact]
    public async Task RemoveAsync_RemoveVolumes_DeletesTheClaimsButNotHostPaths()
    {
        // Arrange
        var spec = new V1PodSpec
        {
            Containers = [new V1Container { Name = "web", Image = "nginx" }],
            Volumes =
            [
                new V1Volume { Name = "claim", PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource { ClaimName = "pvc-1" } },
                new V1Volume { Name = "gone", PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource { ClaimName = "pvc-gone" } },
                new V1Volume { Name = "host", HostPath = new V1HostPathVolumeSource { Path = "/srv" } },
                new V1Volume { Name = "scratch", EmptyDir = new V1EmptyDirVolumeSource() },
            ]
        };
        _api.On(HttpMethod.Get, $"{Pods}/web", Pod("web", spec: spec))
            .On(HttpMethod.Delete, $"{Pods}/web", Pod("web"))
            .On(HttpMethod.Delete, $"{Pvcs}/pvc-1", new V1PersistentVolumeClaim());

        // Act
        await Containers().RemoveAsync("web", removeVolumes: true);

        // Assert
        var deletes = _api.Requests.Where(r => r.Method == HttpMethod.Delete).Select(r => r.Path).ToList();
        deletes.ShouldBe([$"{Pods}/web", $"{Pvcs}/pvc-1", $"{Pvcs}/pvc-gone"]);
    }

    [Fact]
    public async Task RemoveAsync_RemoveVolumesForAPodAlreadyGone_StillDeletes()
    {
        // Arrange
        _api.On(HttpMethod.Delete, $"{Pods}/web", Pod("web"));

        // Act
        await Containers().RemoveAsync("web", removeVolumes: true);

        // Assert
        _api.Requests.Select(r => r.Method).ShouldBe([HttpMethod.Get, HttpMethod.Delete]);
    }

    [Fact]
    public async Task GetLogsAsync_ReturnsThePodLog()
    {
        // Arrange
        _api.On(HttpMethod.Get, $"{Pods}/web/log", "line one\nline two");

        // Act
        var logs = await Containers().GetLogsAsync("web");

        // Assert
        logs.ShouldBe("line one\nline two");
    }

    [Fact]
    public void ContainerManager_ReportsProviderAndHost()
    {
        // Act
        var manager = Containers();

        // Assert
        manager.ProviderName.ShouldBe("Kubernetes");
        manager.HostAddress.ShouldBe("localhost");
    }

    // -----------------------------------------------------------------------
    // Images (node image lists)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ImageListAsync_CollectsTheImagesOfEveryNode()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/api/v1/nodes", new V1NodeList
        {
            Items =
            [
                new V1Node { Status = new V1NodeStatus { Images = [new V1ContainerImage { Names = ["nginx@sha256:1", "nginx:1.25"], SizeBytes = 42 }] } },
                new V1Node { Status = new V1NodeStatus { Images = [new V1ContainerImage()] } },
                new V1Node { Status = new V1NodeStatus() },
                new V1Node(),
            ]
        });
        var images = new KubernetesImageManager(_client, NullLogger<KubernetesImageManager>.Instance);

        // Act
        var list = await images.ListAsync();

        // Assert
        list.Count.ShouldBe(2);
        list[0].Id.ShouldBe("nginx@sha256:1");
        list[0].Tags.ShouldBe(["nginx@sha256:1", "nginx:1.25"]);
        list[0].Size.ShouldBe(42);
        list[1].Id.ShouldBeEmpty();
        list[1].Tags.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImageManager_UnsupportedOperations_AreNoOpsOrThrow()
    {
        // Arrange
        var images = new KubernetesImageManager(_client, NullLogger<KubernetesImageManager>.Instance);

        // Act & Assert
        (await images.GetAsync("nginx")).ShouldBeNull();
        await images.PullAsync(new PullImageRequest { Image = "nginx", Tag = "1.25" });
        await Should.ThrowAsync<NotSupportedException>(() => images.RemoveAsync("nginx"));
        await Should.ThrowAsync<NotSupportedException>(() => images.TagAsync("nginx", "repo", "v1"));
        _api.Requests.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Networks (services)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NetworkListAsync_MapsServices()
    {
        // Arrange
        _api.On(HttpMethod.Get, Services, new V1ServiceList
        {
            Items =
            [
                new V1Service { Metadata = new V1ObjectMeta { Name = "web", Uid = "svc-1", Labels = new Dictionary<string, string> { ["a"] = "b" } }, Spec = new V1ServiceSpec { Type = "NodePort" } },
                new V1Service { Metadata = new V1ObjectMeta { Name = "db" }, Spec = new V1ServiceSpec() },
            ]
        });
        var networks = new KubernetesNetworkManager(_client, _options, NullLogger<KubernetesNetworkManager>.Instance);

        // Act
        var list = await networks.ListAsync();

        // Assert
        list[0].Id.ShouldBe("svc-1");
        list[0].Driver.ShouldBe("NodePort");
        list[0].Labels["a"].ShouldBe("b");
        list[1].Id.ShouldBeEmpty();
        list[1].Driver.ShouldBe("ClusterIP");
        list[1].Labels.ShouldBeEmpty();
    }

    [Fact]
    public async Task NetworkCreateAndRemove_ManageServices()
    {
        // Arrange
        _api.On(HttpMethod.Post, Services, r =>
            {
                var service = r.BodyAs<V1Service>();
                service.Metadata.Uid = "svc-new";
                return (HttpStatusCode.Created, service);
            })
            .On(HttpMethod.Delete, $"{Services}/web", new V1Service());
        var networks = new KubernetesNetworkManager(_client, _options, NullLogger<KubernetesNetworkManager>.Instance);

        // Act
        var id = await networks.CreateAsync("web", "LoadBalancer");
        await networks.RemoveAsync("web");
        await networks.ConnectAsync("web", "pod-1", ["alias"]);
        await networks.DisconnectAsync("web", "pod-1");

        // Assert
        id.ShouldBe("svc-new");
        var created = _api.Single(HttpMethod.Post, Services).BodyAs<V1Service>();
        created.Metadata.Name.ShouldBe("web");
        created.Spec.Type.ShouldBe("LoadBalancer");
        _api.Requests.Count.ShouldBe(2);
    }

    // -----------------------------------------------------------------------
    // Volumes (persistent volume claims)
    // -----------------------------------------------------------------------

    private static V1PersistentVolumeClaim Claim(string name, IDictionary<string, string>? labels = null) => new()
    {
        Metadata = new V1ObjectMeta { Name = name, Labels = labels, CreationTimestamp = new DateTime(2026, 5, 6, 0, 0, 0, DateTimeKind.Utc) },
        Spec = new V1PersistentVolumeClaimSpec { StorageClassName = "fast", VolumeName = "pv-1" }
    };

    [Fact]
    public async Task VolumeListAndGet_MapClaims()
    {
        // Arrange
        _api.On(HttpMethod.Get, Pvcs, new V1PersistentVolumeClaimList
        {
            Items = [Claim("data", new Dictionary<string, string> { ["x"] = "y" }), new V1PersistentVolumeClaim { Metadata = new V1ObjectMeta(), Spec = new V1PersistentVolumeClaimSpec() }]
        })
            .On(HttpMethod.Get, $"{Pvcs}/data", Claim("data"));
        var volumes = new KubernetesVolumeManager(_client, _options, NullLogger<KubernetesVolumeManager>.Instance);

        // Act
        var list = await volumes.ListAsync();
        var one = await volumes.GetAsync("data");
        var missing = await volumes.GetAsync("missing");

        // Assert
        list[0].Name.ShouldBe("data");
        list[0].Driver.ShouldBe("fast");
        list[0].MountPoint.ShouldBe("pv-1");
        list[0].Labels["x"].ShouldBe("y");
        list[1].Name.ShouldBeEmpty();
        list[1].Driver.ShouldBeEmpty();
        list[1].CreatedAt.ShouldBe(DateTimeOffset.MinValue);
        one!.CreatedAt.ShouldBe(new DateTimeOffset(2026, 5, 6, 0, 0, 0, TimeSpan.Zero));
        one.Labels.ShouldBeEmpty();
        missing.ShouldBeNull();
    }

    [Fact]
    public async Task VolumeCreateAndRemove_ManageClaims()
    {
        // Arrange
        _api.On(HttpMethod.Post, Pvcs, r =>
            {
                var claim = r.BodyAs<V1PersistentVolumeClaim>();
                claim.Metadata.Labels = new Dictionary<string, string> { ["created"] = "yes" };
                return (HttpStatusCode.Created, claim);
            })
            .On(HttpMethod.Delete, $"{Pvcs}/data", Claim("data"));
        var volumes = new KubernetesVolumeManager(_client, _options, NullLogger<KubernetesVolumeManager>.Instance);

        // Act
        var created = await volumes.CreateAsync("data", "fast");
        await volumes.RemoveAsync("data");

        // Assert
        created.Name.ShouldBe("data");
        created.Driver.ShouldBe("fast");
        created.Labels["created"].ShouldBe("yes");
        var claim = _api.Single(HttpMethod.Post, Pvcs).BodyAs<V1PersistentVolumeClaim>();
        claim.Spec.AccessModes.ShouldBe(["ReadWriteOnce"]);
        claim.Spec.Resources.Requests["storage"].ToString().ShouldBe("1Gi");
        _api.Requests.Last().Method.ShouldBe(HttpMethod.Delete);
    }

    [Fact]
    public async Task VolumeCreate_WithoutDriver_LeavesTheStorageClassUnset()
    {
        // Arrange
        _api.On(HttpMethod.Post, Pvcs, r => (HttpStatusCode.Created, r.BodyAs<V1PersistentVolumeClaim>()));
        var volumes = new KubernetesVolumeManager(_client, _options, NullLogger<KubernetesVolumeManager>.Instance);

        // Act
        var created = await volumes.CreateAsync("data");

        // Assert
        created.Driver.ShouldBeEmpty();
        created.Labels.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Orchestrator and registration
    // -----------------------------------------------------------------------

    private KubernetesContainerOrchestrator Orchestrator() => new(
        Containers(),
        new KubernetesImageManager(_client, NullLogger<KubernetesImageManager>.Instance),
        new KubernetesNetworkManager(_client, _options, NullLogger<KubernetesNetworkManager>.Instance),
        new KubernetesVolumeManager(_client, _options, NullLogger<KubernetesVolumeManager>.Instance),
        _client,
        NullLogger<KubernetesContainerOrchestrator>.Instance);

    [Fact]
    public async Task IsAvailableAsync_ApiAnswers_ReturnsTrue()
    {
        // Arrange
        _api.On(HttpMethod.Get, "/api/v1/", new V1APIResourceList { GroupVersion = "v1", Resources = [] });

        // Act & Assert
        (await Orchestrator().IsAvailableAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_ApiFails_ReturnsFalse()
    {
        // Act
        var orchestrator = Orchestrator();

        // Assert
        (await orchestrator.IsAvailableAsync()).ShouldBeFalse();
        orchestrator.ProviderName.ShouldBe("Kubernetes");
        orchestrator.Containers.ShouldBeOfType<KubernetesContainerManager>();
        orchestrator.Images.ShouldBeOfType<KubernetesImageManager>();
        orchestrator.Networks.ShouldBeOfType<KubernetesNetworkManager>();
        orchestrator.Volumes.ShouldBeOfType<KubernetesVolumeManager>();
    }

    private static string WriteKubeConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kubeconfig-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            apiVersion: v1
            kind: Config
            current-context: test
            clusters:
            - name: test
              cluster:
                server: https://cluster.test:6443
            contexts:
            - name: test
              context:
                cluster: test
                user: test
            users:
            - name: test
              user:
                token: secret
            """);
        return path;
    }

    [Fact]
    public void AddKubernetes_KubeConfigFile_BuildsAClientForItsCluster()
    {
        // Arrange
        var kubeConfig = WriteKubeConfig();
        var services = new ServiceCollection().AddLogging();

        try
        {
            // Act
            services.AddMicroservicesOrchestrator(b => b.AddKubernetes(o =>
            {
                o.KubeConfigPath = kubeConfig;
                o.Namespace = "apps";
            }));
            using var provider = services.BuildServiceProvider();
            var client = provider.GetRequiredService<IKubernetes>();

            // Assert
            client.BaseUri.ShouldBe(new Uri("https://cluster.test:6443"));
            provider.GetRequiredService<KubernetesOptions>().Namespace.ShouldBe("apps");
            provider.GetRequiredService<IContainerOrchestrator>().ShouldBeOfType<KubernetesContainerOrchestrator>();
        }
        finally
        {
            File.Delete(kubeConfig);
        }
    }

    [Fact]
    public void AddKubernetes_ApiServerUrl_OverridesTheConfiguredHost()
    {
        // Arrange
        var kubeConfig = WriteKubeConfig();
        var services = new ServiceCollection().AddLogging();

        try
        {
            // Act
            services.AddMicroservicesOrchestrator(b => b.AddKubernetes(o =>
            {
                o.KubeConfigPath = kubeConfig;
                o.ApiServerUrl = "https://override.test";
            }));
            using var provider = services.BuildServiceProvider();

            // Assert
            provider.GetRequiredService<IKubernetes>().BaseUri.ShouldBe(new Uri("https://override.test"));
        }
        finally
        {
            File.Delete(kubeConfig);
        }
    }

    [Fact]
    public void AddKubernetes_InClusterOutsideACluster_FailsToBuildTheClient()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging();
        services.AddMicroservicesOrchestrator(b => b.AddKubernetes(o => o.UseInClusterConfig = true));
        using var provider = services.BuildServiceProvider();

        // Act & Assert
        Should.Throw<Exception>(() => provider.GetRequiredService<IKubernetes>());
    }
}

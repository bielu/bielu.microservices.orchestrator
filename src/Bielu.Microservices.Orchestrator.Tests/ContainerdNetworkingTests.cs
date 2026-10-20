using System.Diagnostics;
using System.Text.Json.Nodes;
using Bielu.Microservices.Orchestrator.Containerd;
using Bielu.Microservices.Orchestrator.Containerd.Configuration;
using Containerd.Services.Tasks.V1;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using TaskProcess = Containerd.V1.Types.Process;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="ContainerdNetworkManager"/> (CNI config files in a temporary directory) and
/// <see cref="CniPluginInvoker"/> (the CNI wire protocol, with the plugin process substituted).
/// </summary>
public sealed class ContainerdNetworkingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bielu-cni-").FullName;
    private readonly string _configDir;
    private readonly string _binDir;
    private readonly FakeContainerd _grpc = new();
    private readonly List<PluginRun> _runs = [];
    private readonly Queue<CniPluginInvoker.ProcessResult> _results = new();

    public ContainerdNetworkingTests()
    {
        _configDir = Path.Combine(_root, "net.d");
        _binDir = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A plugin execution observed through the substituted process runner.</summary>
    private sealed record PluginRun(string Binary, IDictionary<string, string?> Environment, JsonNode Config);

    private Task<CniPluginInvoker.ProcessResult> RecordRun(ProcessStartInfo startInfo, string stdin, CancellationToken _)
    {
        _runs.Add(new PluginRun(startInfo.FileName, new Dictionary<string, string?>(startInfo.Environment), JsonNode.Parse(stdin)!));
        return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : new CniPluginInvoker.ProcessResult(0, "", ""));
    }

    private CniPluginInvoker Invoker() => new(NullLogger.Instance, RecordRun);

    private void InstallPlugins(params string[] names)
    {
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(_binDir, name), "");
        }
    }

    private ContainerdNetworkManager Manager() => new(
        new Tasks.TasksClient(_grpc),
        new ContainerdOptions { Namespace = "apps", CniConfigPath = _configDir, CniBinPath = _binDir, CniDefaultSubnet = "10.99.0.0/16" },
        NullLogger<ContainerdNetworkManager>.Instance)
    {
        CniInvokerFactory = log => new CniPluginInvoker(log, RecordRun)
    };

    private void WriteConfig(string fileName, string json)
    {
        Directory.CreateDirectory(_configDir);
        File.WriteAllText(Path.Combine(_configDir, fileName), json);
    }

    private void TaskWithPid(uint pid) =>
        _grpc.On<GetRequest, GetResponse>(FakeContainerd.Tasks, "Get", _ => new GetResponse { Process = new TaskProcess { Pid = pid } });

    // -----------------------------------------------------------------------
    // Network config files
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_NoConfigDirectory_ReturnsNothing()
    {
        // Act & Assert
        (await Manager().ListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAsync_ConfigFiles_AreReadInOrderAndBrokenOnesSkipped()
    {
        // Arrange
        WriteConfig("10-bridge.conf", """{"name":"bridge-net","type":"bridge"}""");
        WriteConfig("20-chain.conflist", """{"name":"chain-net","plugins":[{"type":"macvlan"},{"type":"portmap"}]}""");
        WriteConfig("30-mystery.json", """{"name":"mystery-net"}""");
        WriteConfig("40-nameless.conf", """{"type":"bridge"}""");
        WriteConfig("50-broken.conf", "{ not json");
        WriteConfig("60-null.conf", "null");
        WriteConfig("README.md", """{"name":"ignored"}""");

        // Act
        var networks = await Manager().ListAsync();

        // Assert
        networks.Select(n => (n.Id, n.Name, n.Driver)).ShouldBe(
        [
            ("10-bridge", "bridge-net", "bridge"),
            ("20-chain", "chain-net", "macvlan"),
            ("30-mystery", "mystery-net", "unknown"),
        ]);
        networks[0].Labels["cni.config.file"].ShouldEndWith("10-bridge.conf");
    }

    [Fact]
    public async Task ListAsync_Cancelled_Throws()
    {
        // Arrange
        WriteConfig("10-bridge.conf", """{"name":"bridge-net","type":"bridge"}""");

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(() => Manager().ListAsync(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task CreateAsync_WritesABridgeConfigWithHostLocalIpam()
    {
        // Act
        var id = await Manager().CreateAsync("app-net", "bridge");

        // Assert
        id.ShouldBe("10-app-net");
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_configDir, "10-app-net.conf")))!;
        config["name"]!.GetValue<string>().ShouldBe("app-net");
        config["type"]!.GetValue<string>().ShouldBe("bridge");
        config["bridge"]!.GetValue<string>().ShouldBe("cni-app-net");
        config["ipam"]!["type"]!.GetValue<string>().ShouldBe("host-local");
        config["ipam"]!["ranges"]![0]![0]!["subnet"]!.GetValue<string>().ShouldBe("10.99.0.0/16");
        (await Manager().ListAsync()).ShouldHaveSingleItem().Name.ShouldBe("app-net");
    }

    [Fact]
    public async Task RemoveAsync_DeletesConfigsMatchingTheIdOrTheName()
    {
        // Arrange
        WriteConfig("10-by-id.conf", """{"name":"first","type":"bridge"}""");
        WriteConfig("20-other.conf", """{"name":"second","type":"bridge"}""");
        WriteConfig("30-broken.conf", "{ not json");
        WriteConfig("40-keep.conf", """{"name":"keep","type":"bridge"}""");

        // Act
        await Manager().RemoveAsync("10-by-id");
        await Manager().RemoveAsync("second");
        await Manager().RemoveAsync("nothing-matches");

        // Assert
        Directory.GetFiles(_configDir).Select(Path.GetFileName).Order().ShouldBe(["30-broken.conf", "40-keep.conf"]);
    }

    [Fact]
    public async Task RemoveAsync_NoConfigDirectory_IsANoOp()
    {
        // Act & Assert
        await Should.NotThrowAsync(() => Manager().RemoveAsync("anything"));
    }

    // -----------------------------------------------------------------------
    // Connect / disconnect
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_RunsCniAddInTheTaskNetworkNamespace()
    {
        // Arrange
        InstallPlugins("bridge");
        WriteConfig("10-app-net.conf", """{"cniVersion":"1.0.0","name":"app-net","type":"bridge"}""");
        TaskWithPid(4321);

        // Act
        await Manager().ConnectAsync("app-net", "web");

        // Assert
        var run = _runs.ShouldHaveSingleItem();
        run.Binary.ShouldBe(Path.Combine(_binDir, "bridge"));
        run.Environment["CNI_COMMAND"].ShouldBe("ADD");
        run.Environment["CNI_CONTAINERID"].ShouldBe("web");
        run.Environment["CNI_NETNS"].ShouldBe("/proc/4321/ns/net");
        run.Environment["CNI_IFNAME"].ShouldBe("eth0");
        run.Environment["CNI_PATH"].ShouldBe(_binDir);
        run.Config["name"]!.GetValue<string>().ShouldBe("app-net");
        var getTask = _grpc.Calls.ShouldHaveSingleItem();
        ((GetRequest)getTask.Request).ContainerId.ShouldBe("web");
        getTask.Namespace.ShouldBe("apps");
    }

    [Fact]
    public async Task DisconnectAsync_RunsCniDelForTheConfigFoundByFileName()
    {
        // Arrange
        InstallPlugins("bridge");
        WriteConfig("05-broken.conf", "{ not json");
        WriteConfig("10-app-net.conf", """{"name":"app-net","type":"bridge"}""");
        TaskWithPid(77);

        // Act
        await Manager().DisconnectAsync("10-app-net", "web");

        // Assert
        _runs.ShouldHaveSingleItem().Environment["CNI_COMMAND"].ShouldBe("DEL");
    }

    [Fact]
    public async Task ConnectAsync_UnknownNetwork_Throws()
    {
        // Arrange
        WriteConfig("10-app-net.conf", """{"name":"app-net","type":"bridge"}""");

        // Act & Assert
        await Should.ThrowAsync<KeyNotFoundException>(() => Manager().ConnectAsync("other-net", "web"));
    }

    [Fact]
    public async Task ConnectAsync_NoConfigDirectory_Throws()
    {
        // Act & Assert
        await Should.ThrowAsync<DirectoryNotFoundException>(() => Manager().ConnectAsync("app-net", "web"));
    }

    [Fact]
    public async Task ConnectAsync_Cancelled_Throws()
    {
        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => Manager().ConnectAsync("app-net", "web", cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ConnectAsync_TaskNotRunning_ExplainsThatTheContainerMustBeStarted()
    {
        // Arrange
        WriteConfig("10-app-net.conf", """{"name":"app-net","type":"bridge"}""");
        _grpc.Fail(FakeContainerd.Tasks, "Get", StatusCode.NotFound);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => Manager().ConnectAsync("app-net", "web"));
        ex.Message.ShouldContain("Start the container");
        ex.InnerException.ShouldBeOfType<RpcException>();
    }

    [Fact]
    public async Task ConnectAsync_TaskWithoutPid_Throws()
    {
        // Arrange
        WriteConfig("10-app-net.conf", """{"name":"app-net","type":"bridge"}""");
        TaskWithPid(0);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => Manager().ConnectAsync("app-net", "web"));
        ex.Message.ShouldContain("PID 0");
    }

    [Fact]
    public async Task ConnectAsync_TaskWithoutProcess_Throws()
    {
        // Arrange
        WriteConfig("10-app-net.conf", """{"name":"app-net","type":"bridge"}""");
        _grpc.On<GetRequest, GetResponse>(FakeContainerd.Tasks, "Get", _ => new GetResponse());

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => Manager().ConnectAsync("app-net", "web"));
        ex.Message.ShouldContain("no PID");
    }

    // -----------------------------------------------------------------------
    // CNI wire protocol
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddAsync_ConfList_RunsPluginsInOrderChainingPrevResult()
    {
        // Arrange
        InstallPlugins("bridge", "portmap");
        _results.Enqueue(new(0, """{"cniVersion":"1.0.0","ips":[{"address":"10.88.0.5/16"}]}""", "bridge: note"));
        _results.Enqueue(new(0, "  ", ""));
        const string conflist = """
            {"cniVersion":"0.4.0","name":"chain","plugins":[
              {"type":"bridge","bridge":"cni0"},
              null,
              {"type":"portmap","capabilities":{"portMappings":true}}
            ]}
            """;

        // Act
        await Invoker().AddAsync("web", "/proc/1/ns/net", conflist, _binDir);

        // Assert
        _runs.Select(r => r.Binary).ShouldBe([Path.Combine(_binDir, "bridge"), Path.Combine(_binDir, "portmap")]);
        _runs[0].Config["cniVersion"]!.GetValue<string>().ShouldBe("0.4.0");
        _runs[0].Config["name"]!.GetValue<string>().ShouldBe("chain");
        _runs[0].Config["bridge"]!.GetValue<string>().ShouldBe("cni0");
        _runs[0].Config["prevResult"].ShouldBeNull();
        _runs[1].Config["prevResult"]!["ips"]![0]!["address"]!.GetValue<string>().ShouldBe("10.88.0.5/16");
    }

    [Fact]
    public async Task DeleteAsync_ConfList_RunsPluginsInReverseWithoutPrevResult()
    {
        // Arrange
        InstallPlugins("bridge", "portmap");
        _results.Enqueue(new(0, """{"ignored":true}""", ""));
        const string conflist = """{"plugins":[{"type":"bridge"},{"type":"portmap"}]}""";

        // Act
        await Invoker().DeleteAsync("web", "/proc/1/ns/net", conflist, _binDir);

        // Assert
        _runs.Select(r => Path.GetFileName(r.Binary)).ShouldBe(["portmap", "bridge"]);
        _runs.ShouldAllBe(r => r.Config["prevResult"] == null && r.Environment["CNI_COMMAND"] == "DEL");
        _runs[0].Config["cniVersion"]!.GetValue<string>().ShouldBe("1.0.0");
        _runs[0].Config["name"]!.GetValue<string>().ShouldBe("");
    }

    [Fact]
    public async Task AddAsync_InvalidConfig_Throws()
    {
        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() => Invoker().AddAsync("web", "/netns", "null", _binDir));
        await Should.ThrowAsync<InvalidOperationException>(() => Invoker().DeleteAsync("web", "/netns", "null", _binDir));
    }

    [Fact]
    public async Task AddAsync_ConfigWithoutType_Throws()
    {
        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => Invoker().AddAsync("web", "/netns", """{"name":"x"}""", _binDir));
        ex.Message.ShouldContain("'type'");
    }

    [Theory]
    [InlineData("missing-plugin")]
    [InlineData("../escape")]
    [InlineData("nested\\\\escape")]
    public async Task AddAsync_PluginBinaryNotFound_Throws(string type)
    {
        // Act & Assert
        await Should.ThrowAsync<FileNotFoundException>(
            () => Invoker().AddAsync("web", "/netns", $$"""{"name":"x","type":"{{type}}"}""", _binDir));
        _runs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"code":11,"msg":"no IPs left","details":"range exhausted"}""", "stderr text", "no IPs left: range exhausted")]
    [InlineData("""{"code":11,"msg":"no IPs left"}""", "stderr text", "no IPs left")]
    [InlineData("""{"code":11}""", "stderr text", "stderr text")]
    [InlineData("plain failure", "stderr text", "stderr text")]
    [InlineData("", "stderr text", "stderr text")]
    public async Task AddAsync_PluginFails_ReportsTheCniErrorOrStderr(string stdout, string stderr, string expected)
    {
        // Arrange
        InstallPlugins("bridge");
        _results.Enqueue(new(7, stdout, stderr));

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => Invoker().AddAsync("web", "/netns", """{"name":"x","type":"bridge"}""", _binDir));
        ex.Message.ShouldContain("exit 7");
        ex.Message.ShouldEndWith(expected);
    }

    [UnixOnlyFact]
    public async Task AddAsync_RealPluginProcess_ReceivesTheConfigOnStdinAndTheCniEnvironment()
    {
        // Arrange: a shell "plugin" that records its stdin and environment, then prints a result.
        var plugin = Path.Combine(_binDir, "recorder");
        File.WriteAllText(plugin, """
            #!/bin/sh
            cat > "$0.stdin"
            echo "$CNI_COMMAND $CNI_CONTAINERID $CNI_NETNS $CNI_IFNAME" > "$0.env"
            echo "diagnostic" >&2
            echo '{"cniVersion":"1.0.0"}'
            """.Replace("\r\n", "\n"));
        File.SetUnixFileMode(plugin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var invoker = new CniPluginInvoker(NullLogger.Instance);

        // Act
        await invoker.AddAsync("web", "/proc/9/ns/net", """{"name":"x","type":"recorder"}""", _binDir);

        // Assert
        JsonNode.Parse(File.ReadAllText(plugin + ".stdin"))!["type"]!.GetValue<string>().ShouldBe("recorder");
        File.ReadAllText(plugin + ".env").Trim().ShouldBe("ADD web /proc/9/ns/net eth0");
    }

    [UnixOnlyFact]
    public async Task AddAsync_RealPluginProcessFails_ReportsItsError()
    {
        // Arrange
        var plugin = Path.Combine(_binDir, "failing");
        File.WriteAllText(plugin, "#!/bin/sh\ncat > /dev/null\necho '{\"msg\":\"boom\"}'\nexit 3\n");
        File.SetUnixFileMode(plugin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var invoker = new CniPluginInvoker(NullLogger.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => invoker.AddAsync("web", "/netns", """{"name":"x","type":"failing"}""", _binDir));
        ex.Message.ShouldContain("exit 3");
        ex.Message.ShouldEndWith("boom");
    }
}

/// <summary>A fact that only runs where shell-script executables work (Linux and macOS).</summary>
public sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Requires a Unix shell to run a fake CNI plugin binary.";
        }
    }
}

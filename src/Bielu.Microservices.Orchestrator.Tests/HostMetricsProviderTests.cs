using Bielu.Microservices.Orchestrator.OpenTelemetry;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Extensions;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="HostMetricsProvider"/>, using fixture copies of <c>/proc/stat</c> and
/// <c>/proc/meminfo</c> so the Linux parsing runs on every OS, and for the OpenTelemetry SDK
/// registration helpers.
/// </summary>
public sealed class HostMetricsProviderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("bielu-host-metrics-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Fixture(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string MissingFile => Path.Combine(_directory, "missing");

    private HostMetricsProvider ProcProvider(string? stat = null, string? memInfo = null) =>
        new(stat ?? MissingFile, memInfo ?? MissingFile, useProcFileSystem: true);

    [Fact]
    public void GetCpuUsagePercent_TwoSamples_ComputesBusyShareOfTheDelta()
    {
        // Arrange: between the samples 100 jiffies pass, 25 of them idle.
        var stat = Fixture("stat", "cpu  100 0 100 800 0 0 0 0 0 0\ncpu0 1 2 3 4\n");
        var provider = ProcProvider(stat: stat);
        provider.GetCpuUsagePercent();
        File.WriteAllText(stat, "cpu  150 0 125 825 0 0 0 0 0 0\n");

        // Act
        var usage = provider.GetCpuUsagePercent();

        // Assert
        usage.ShouldBe(75);
    }

    [Fact]
    public void GetCpuUsagePercent_NoTimePassed_ReturnsThePreviousValue()
    {
        // Arrange
        var stat = Fixture("stat", "cpu  100 0 100 800\n");
        var provider = ProcProvider(stat: stat);
        provider.GetCpuUsagePercent();
        File.WriteAllText(stat, "cpu  200 0 100 900\n");
        var previous = provider.GetCpuUsagePercent();

        // Act
        var usage = provider.GetCpuUsagePercent();

        // Assert
        previous.ShouldBe(50);
        usage.ShouldBe(previous);
    }

    [Theory]
    [InlineData("intr 1 2 3\n")]
    [InlineData("cpu  1 2 3\n")]
    [InlineData("cpu  1 2 3 idle 5\n")]
    public void GetCpuUsagePercent_UnusableStatLine_ReturnsTheLastValue(string content)
    {
        // Arrange
        var provider = ProcProvider(stat: Fixture("stat", content));

        // Act & Assert
        provider.GetCpuUsagePercent().ShouldBe(0);
    }

    [Fact]
    public void GetCpuUsagePercent_StatFileMissing_FallsBackToProcessUsage()
    {
        // Arrange
        var provider = ProcProvider();

        // Act
        var usage = provider.GetCpuUsagePercent();

        // Assert
        usage.ShouldBeInRange(0, 100 * Environment.ProcessorCount);
    }

    [Fact]
    public void GetCpuUsagePercent_WithoutProcFileSystem_UsesProcessUsage()
    {
        // Arrange
        var provider = new HostMetricsProvider(MissingFile, MissingFile, useProcFileSystem: false);

        // Act
        var usage = provider.GetCpuUsagePercent();

        // Assert
        usage.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void MemoryMetrics_FromMemInfo_ConvertKilobytesAndComputeUsage()
    {
        // Arrange
        var memInfo = Fixture("meminfo", "MemTotal:        8000 kB\nMemFree:  100 kB\nMemAvailable:     2000 kB\nNoUnit:42\n");
        var provider = ProcProvider(memInfo: memInfo);

        // Act & Assert
        provider.GetTotalMemoryBytes().ShouldBe(8000 * 1024);
        provider.GetAvailableMemoryBytes().ShouldBe(2000 * 1024);
        provider.GetMemoryUsagePercent().ShouldBe(75);
    }

    [Fact]
    public void MemoryMetrics_ValueWithoutUnit_IsParsed()
    {
        // Arrange
        var provider = ProcProvider(memInfo: Fixture("meminfo", "MemTotal:4096\nMemAvailable:1024\n"));

        // Act & Assert
        provider.GetTotalMemoryBytes().ShouldBe(4096 * 1024);
        provider.GetAvailableMemoryBytes().ShouldBe(1024 * 1024);
    }

    [Fact]
    public void MemoryMetrics_MemInfoMissingOrUnparsable_FallBackToGcInformation()
    {
        // Arrange
        var missing = ProcProvider();
        var garbage = ProcProvider(memInfo: Fixture("meminfo", "MemTotal: lots kB\n"));
        var gcTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        // Act & Assert
        missing.GetTotalMemoryBytes().ShouldBe(gcTotal);
        garbage.GetTotalMemoryBytes().ShouldBe(gcTotal);
        missing.GetAvailableMemoryBytes().ShouldBeInRange(0, gcTotal);
        missing.GetMemoryUsagePercent().ShouldBeInRange(0, 100);
    }

    [Fact]
    public void MemoryMetrics_WithoutProcFileSystem_UseGcInformation()
    {
        // Arrange
        var provider = new HostMetricsProvider(MissingFile, MissingFile, useProcFileSystem: false);

        // Act & Assert
        provider.GetTotalMemoryBytes().ShouldBe(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        provider.GetMemoryUsagePercent().ShouldBeInRange(0, 100);
    }

    [Fact]
    public void AddOrchestratorInstrumentation_RegistersTheActivitySource()
    {
        // Arrange
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddOrchestratorInstrumentation()
            .Build();

        // Act
        using var activity = OrchestratorActivitySource.Source.StartActivity("registration-check");

        // Assert
        activity.ShouldNotBeNull();
    }

    [Fact]
    public void AddOrchestratorMetrics_ReturnsTheBuilderForChaining()
    {
        // Arrange
        var builder = Sdk.CreateMeterProviderBuilder();

        // Act
        var result = builder.AddOrchestratorMetrics();

        // Assert
        result.ShouldBeSameAs(builder);
        using var meterProvider = result.Build();
        meterProvider.ShouldNotBeNull();
    }
}

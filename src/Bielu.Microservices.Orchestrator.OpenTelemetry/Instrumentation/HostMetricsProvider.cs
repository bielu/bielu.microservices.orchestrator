using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Provides cross-platform host-level CPU and memory metrics.
/// On Linux, reads from <c>/proc/stat</c> and <c>/proc/meminfo</c>.
/// On other platforms, falls back to process-level approximations.
/// </summary>
internal sealed class HostMetricsProvider
{
    private long _previousIdleTime;
    private long _previousTotalTime;
    private double _lastCpuUsagePercent;

    /// <summary>
    /// Gets the current host CPU usage as a percentage (0–100).
    /// </summary>
    internal double GetCpuUsagePercent()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return GetLinuxCpuUsage();
        }

        return GetProcessCpuUsage();
    }

    /// <summary>
    /// Gets the total physical memory of the host in bytes.
    /// </summary>
    internal static long GetTotalMemoryBytes()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var total = ReadMemInfoValue("MemTotal:");
            if (total > 0)
                return total * 1024; // /proc/meminfo reports in kB
        }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    /// <summary>
    /// Gets the available (free) memory of the host in bytes.
    /// </summary>
    internal static long GetAvailableMemoryBytes()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var available = ReadMemInfoValue("MemAvailable:");
            if (available > 0)
                return available * 1024; // /proc/meminfo reports in kB
        }

        // Fallback: total minus current process working set
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var processUsed = Process.GetCurrentProcess().WorkingSet64;
        return Math.Max(0, total - processUsed);
    }

    /// <summary>
    /// Gets the host memory usage as a percentage (0–100).
    /// </summary>
    internal static double GetMemoryUsagePercent()
    {
        var total = GetTotalMemoryBytes();
        if (total <= 0)
            return 0;

        var available = GetAvailableMemoryBytes();
        var used = total - available;
        return Math.Round((double)used / total * 100, 2);
    }

    private double GetLinuxCpuUsage()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
            if (line is null)
                return _lastCpuUsagePercent;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
                return _lastCpuUsagePercent;

            // Fields: cpu user nice system idle iowait irq softirq steal guest guest_nice
            long idle = long.Parse(parts[4]);
            long total = 0;
            for (var i = 1; i < parts.Length; i++)
            {
                if (long.TryParse(parts[i], out var val))
                    total += val;
            }

            var deltaIdle = idle - _previousIdleTime;
            var deltaTotal = total - _previousTotalTime;

            _previousIdleTime = idle;
            _previousTotalTime = total;

            if (deltaTotal == 0)
                return _lastCpuUsagePercent;

            _lastCpuUsagePercent = Math.Round((1.0 - (double)deltaIdle / deltaTotal) * 100, 2);
            return _lastCpuUsagePercent;
        }
        catch
        {
            return GetProcessCpuUsage();
        }
    }

    private static double GetProcessCpuUsage()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            // Rough approximation: ratio of process CPU time to uptime × processor count
            var cpuTime = process.TotalProcessorTime.TotalMilliseconds;
            var uptime = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
            if (uptime <= 0)
                return 0;

            return Math.Round(cpuTime / (uptime * Environment.ProcessorCount) * 100, 2);
        }
        catch
        {
            return 0;
        }
    }

    private static long ReadMemInfoValue(string key)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith(key, StringComparison.Ordinal))
                    continue;

                var valuePart = line[key.Length..].Trim();
                var numberEnd = valuePart.IndexOf(' ');
                var numberStr = numberEnd > 0 ? valuePart[..numberEnd] : valuePart;

                if (long.TryParse(numberStr, out var value))
                    return value;
            }
        }
        catch
        {
            // Silently fall back
        }

        return -1;
    }
}

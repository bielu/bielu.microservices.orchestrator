using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Gateway.Contracts.Models;
using Bielu.Microservices.Orchestrator.Gateway.Registration.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Registration.Extensions;
using Bielu.Microservices.Orchestrator.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Gateway.Registration.Services;

/// <summary>
/// Background service that registers the orchestrator instance with the YARP gateway
/// on startup, sends periodic heartbeats, and deregisters on shutdown.
/// Registration is retried with capped exponential backoff until it succeeds, and the
/// instance re-registers whenever a heartbeat fails (e.g. after a gateway restart).
/// </summary>
public sealed class OrchestratorGatewayRegistrationService(
    IHttpClientFactory httpClientFactory,
    GatewayRegistrationOptions options,
    ILogger<OrchestratorGatewayRegistrationService> logger,
    IContainerOrchestrator? orchestrator = null) : BackgroundService
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DeregisterTimeout = TimeSpan.FromSeconds(10);

    private readonly string _sanitizedId = LogSanitizer.Sanitize(options.InstanceId);
    private readonly string _instancePath = Uri.EscapeDataString(options.InstanceId);
    private TimeSpan _lastCpuTime;
    private DateTimeOffset _lastSampleAt;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Gateway registration service starting for instance {InstanceId}", _sanitizedId);

        var registered = false;
        // Stays true if shutdown interrupts a registration the gateway may already have accepted
        var registering = false;
        var retryDelay = InitialRetryDelay;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                registering = !registered;
                registered = registered
                    ? await TrySendHeartbeatAsync(stoppingToken)
                    : await TryRegisterAsync(stoppingToken);
                registering = false;

                if (registered)
                {
                    retryDelay = InitialRetryDelay;
                    await Task.Delay(options.HeartbeatInterval, stoppingToken);
                }
                else
                {
                    await Task.Delay(retryDelay, stoppingToken);
                    retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — expected
        }
        finally
        {
            if (registered || registering)
                await DeregisterAsync();
        }
    }

    private async Task<bool> TryRegisterAsync(CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(GatewayRegistrationBuilderExtensions.HttpClientName);
            var (cpuPercent, memoryMb) = CollectResourceStats();

            var request = new RegisterRequest
            {
                InstanceId = options.InstanceId,
                Address = options.InstanceAddress,
                Provider = orchestrator?.ProviderName,
                CpuPercent = cpuPercent,
                MemoryMb = memoryMb
            };

            using var response = await client.PostAsJsonAsync("api/gateway/register", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Gateway registration failed with status {StatusCode} for instance {InstanceId}",
                    (int)response.StatusCode, _sanitizedId);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<RegisterResponse>(ct);
            logger.LogInformation(
                "Registered with gateway. TTL={TtlSeconds}s, InstanceId={InstanceId}",
                result?.TtlSeconds, _sanitizedId);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Gateway registration failed for instance {InstanceId}", _sanitizedId);
            return false;
        }
    }

    private async Task<bool> TrySendHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(GatewayRegistrationBuilderExtensions.HttpClientName);
            var (cpuPercent, memoryMb) = CollectResourceStats();

            var heartbeat = new HeartbeatRequest
            {
                CpuPercent = cpuPercent,
                MemoryMb = memoryMb
            };

            using var response = await client.PutAsJsonAsync(
                $"api/gateway/heartbeat/{_instancePath}", heartbeat, ct);

            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning(
                "Heartbeat failed with status {StatusCode} for instance {InstanceId}. Re-registering.",
                (int)response.StatusCode, _sanitizedId);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Heartbeat failed for instance {InstanceId}. Re-registering.", _sanitizedId);
            return false;
        }
    }

    private async Task DeregisterAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(DeregisterTimeout);
            var client = httpClientFactory.CreateClient(GatewayRegistrationBuilderExtensions.HttpClientName);
            using var response = await client.DeleteAsync($"api/gateway/register/{_instancePath}", timeout.Token);

            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation(
                    "Deregistered from gateway. InstanceId={InstanceId}", _sanitizedId);
            }
            else
            {
                logger.LogWarning(
                    "Failed to deregister from gateway for instance {InstanceId}. StatusCode={StatusCode}",
                    _sanitizedId, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to deregister from gateway for instance {InstanceId}", _sanitizedId);
        }
    }

    /// <summary>
    /// Samples process memory and the process CPU usage since the previous sample
    /// (since process start for the first sample), normalized across all logical processors.
    /// </summary>
    private (double CpuPercent, double MemoryMb) CollectResourceStats()
    {
        using var process = Process.GetCurrentProcess();
        var memoryMb = process.WorkingSet64 / (1024.0 * 1024.0);

        var cpuPercent = 0.0;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var cpuTime = process.TotalProcessorTime;

            if (_lastSampleAt == default)
                _lastSampleAt = process.StartTime.ToUniversalTime();

            var elapsed = now - _lastSampleAt;
            if (elapsed > TimeSpan.Zero)
            {
                cpuPercent = (cpuTime - _lastCpuTime).TotalMilliseconds /
                             (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100.0;
            }

            _lastCpuTime = cpuTime;
            _lastSampleAt = now;
        }
        catch (Exception ex) when (ex is NotSupportedException or PlatformNotSupportedException or InvalidOperationException)
        {
            // Some platforms do not expose process CPU times
        }

        return (Math.Round(Math.Clamp(cpuPercent, 0, 100), 2), Math.Round(memoryMb, 2));
    }
}

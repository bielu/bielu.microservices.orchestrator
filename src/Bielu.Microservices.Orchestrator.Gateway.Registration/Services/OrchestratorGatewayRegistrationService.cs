using System.Diagnostics;
using System.Net.Http.Json;
using Bielu.Microservices.Orchestrator.Gateway.Contracts.Models;
using Bielu.Microservices.Orchestrator.Gateway.Registration.Configuration;
using Bielu.Microservices.Orchestrator.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Gateway.Registration.Services;

/// <summary>
/// Background service that registers the orchestrator instance with the YARP gateway
/// on startup, sends periodic heartbeats, and deregisters on shutdown.
/// </summary>
public sealed class OrchestratorGatewayRegistrationService(
    IHttpClientFactory httpClientFactory,
    GatewayRegistrationOptions options,
    ILogger<OrchestratorGatewayRegistrationService> logger) : BackgroundService
{
    private const int MaxRetryAttempts = 5;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sanitizedId = LogSanitizer.Sanitize(options.InstanceId);

        await RegisterWithRetryAsync(sanitizedId, stoppingToken);

        logger.LogInformation(
            "Gateway registration service started for instance {InstanceId}", sanitizedId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(options.HeartbeatInterval, stoppingToken);
                await SendHeartbeatAsync(sanitizedId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — expected
        }
        finally
        {
            await DeregisterAsync(sanitizedId);
        }
    }

    private async Task RegisterWithRetryAsync(string sanitizedId, CancellationToken ct)
    {
        var retryDelay = InitialRetryDelay;

        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                var client = httpClientFactory.CreateClient("GatewayRegistration");
                var (cpuPercent, memoryMb) = CollectResourceStats();

                var request = new RegisterRequest
                {
                    InstanceId = options.InstanceId,
                    Address = options.InstanceAddress,
                    CpuPercent = cpuPercent,
                    MemoryMb = memoryMb
                };

                var response = await client.PostAsJsonAsync("api/gateway/register", request, ct);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<RegisterResponse>(ct);
                if (result is not null)
                {
                    logger.LogInformation(
                        "Registered with gateway. TTL={TtlSeconds}s, InstanceId={InstanceId}",
                        result.TtlSeconds, sanitizedId);
                }

                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Gateway registration attempt {Attempt}/{MaxAttempts} failed for instance {InstanceId}",
                    attempt, MaxRetryAttempts, sanitizedId);

                if (attempt == MaxRetryAttempts)
                {
                    logger.LogError(
                        "Failed to register with gateway after {MaxAttempts} attempts for instance {InstanceId}",
                        MaxRetryAttempts, sanitizedId);
                    return;
                }

                await Task.Delay(retryDelay, ct);
                retryDelay *= 2;
            }
        }
    }

    private async Task SendHeartbeatAsync(string sanitizedId, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("GatewayRegistration");
            var (cpuPercent, memoryMb) = CollectResourceStats();

            var heartbeat = new HeartbeatRequest
            {
                CpuPercent = cpuPercent,
                MemoryMb = memoryMb
            };

            var response = await client.PutAsJsonAsync(
                $"api/gateway/heartbeat/{options.InstanceId}", heartbeat, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Heartbeat failed with status {StatusCode} for instance {InstanceId}. Re-registering.",
                    (int)response.StatusCode, sanitizedId);

                await RegisterWithRetryAsync(sanitizedId, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Heartbeat failed for instance {InstanceId}", sanitizedId);
        }
    }

    private async Task DeregisterAsync(string sanitizedId)
    {
        try
        {
            var client = httpClientFactory.CreateClient("GatewayRegistration");
            await client.DeleteAsync($"api/gateway/register/{options.InstanceId}");

            logger.LogInformation(
                "Deregistered from gateway. InstanceId={InstanceId}", sanitizedId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to deregister from gateway for instance {InstanceId}", sanitizedId);
        }
    }

    private static (double CpuPercent, double MemoryMb) CollectResourceStats()
    {
        var process = Process.GetCurrentProcess();
        var memoryMb = process.WorkingSet64 / (1024.0 * 1024.0);

        // CPU percentage is estimated from total processor time vs wall-clock uptime
        var cpuPercent = 0.0;
        try
        {
            var totalProcessorTime = process.TotalProcessorTime;
            var uptime = DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime();
            if (uptime.TotalMilliseconds > 0)
            {
                cpuPercent = totalProcessorTime.TotalMilliseconds /
                             (uptime.TotalMilliseconds * Environment.ProcessorCount) * 100.0;
            }
        }
        catch
        {
            // Some platforms may not support TotalProcessorTime
        }

        return (Math.Round(cpuPercent, 2), Math.Round(memoryMb, 2));
    }
}

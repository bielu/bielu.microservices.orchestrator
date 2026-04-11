using Bielu.Microservices.Orchestrator.OpenTelemetry;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Aspire service defaults extensions for the orchestrator example.
/// </summary>
public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();

        builder.Services.AddOpenApi("v1");
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry tracing and metrics for the application,
    /// including orchestrator instrumentation and ASP.NET Core instrumentation.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The host application builder for chaining.</returns>
    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        var otel = builder.Services.AddOpenTelemetry();

// Add Metrics for ASP.NET Core and our custom metrics and export via OTLP
        otel.WithMetrics(metrics =>
        {
            // Metrics provider from OpenTelemetry
            metrics.AddAspNetCoreInstrumentation();
            metrics.AddOrchestratorMetrics();
            // Metrics provides by ASP.NET Core in .NET 8
            metrics.AddMeter("Microsoft.AspNetCore.Hosting");
            metrics.AddMeter("Microsoft.AspNetCore.Server.Kestrel");
        });

// Add Tracing for ASP.NET Core and our custom ActivitySource and export via OTLP
        otel.WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation();
            tracing.AddOrchestratorInstrumentation();
        });

// Export OpenTelemetry data via OTLP, using env vars for the configuration
        var OtlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (OtlpEndpoint != null)
        {
            otel.UseOtlpExporter();
        }
  
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapScalarApiReference(option =>
        {
            option.Title = "Royal Villa API";
    
            option
                .AddDocument("v1", "API Version 1.0", "/openapi/v1.json", isDefault: true)
                .AddDocument("v2", "API Version 2.0", "/openapi/v2.json");
        });
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}

using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    private const string HealthPathPrefix = "/health";
    private const string ReadinessEndpointPath = "/health/ready";
    private const string LivenessEndpointPath = "/health/live";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Every outbound HTTP call gets timeouts, bounded retries with jitter and a circuit breaker (ES-043, ES-044).
            http.AddStandardResilienceHandler();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthPathPrefix)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // In Azure, traces and metrics go to Application Insights, authenticated with the workload's managed identity
        // rather than the connection string's instrumentation key (ES-024, ADR 0010). Logs stay on stdout (Serilog),
        // which the Container Apps environment sends to Log Analytics.
        var appInsights = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(appInsights))
        {
            var credential = WorkloadIdentity.Credential(builder.Configuration);
            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddAzureMonitorTraceExporter(o => { o.ConnectionString = appInsights; o.Credential = credential; }))
                .WithMetrics(metrics => metrics.AddAzureMonitorMetricExporter(o => { o.ConnectionString = appInsights; o.Credential = credential; }));
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps liveness and readiness probes in every environment so the container host can use them (ES-030).
    /// The default response writer returns only the overall status (Healthy, Degraded, Unhealthy), never check details,
    /// so the endpoints disclose nothing beyond availability.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Readiness: every check except those tagged "schema" must pass before the instance receives traffic.
        // Schema drift is reported on its own endpoint (see the web project) so it alerts without taking the site down.
        app.MapHealthChecks(ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => !r.Tags.Contains("schema")
        });

        // Liveness: only checks tagged "live"; a failing dependency must not get a healthy process restarted.
        app.MapHealthChecks(LivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}

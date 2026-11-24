using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Umbraco.Cms.Core.Composing;

namespace UmbracoSample.Web.Health;

/// <summary>Registers site-specific health checks.</summary>
public sealed class HealthComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.Services.AddHealthChecks()
            // ES-043: every check has a deadline. Readiness includes the runtime check; drift has its own endpoint.
            .AddCheck<UmbracoRuntimeHealthCheck>("umbraco-runtime", timeout: TimeSpan.FromSeconds(5))
            .AddCheck<SchemaDriftHealthCheck>("schema-drift", tags: [SchemaDriftHealthCheck.Tag], timeout: TimeSpan.FromSeconds(10));
}

/// <summary>Maps the schema drift endpoint.</summary>
public static class HealthEndpoints
{
    public const string SchemaDriftPath = "/health/schema";

    public static WebApplication MapSchemaDriftEndpoint(this WebApplication app)
    {
        app.MapHealthChecks(SchemaDriftPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(SchemaDriftHealthCheck.Tag),
        });

        return app;
    }
}

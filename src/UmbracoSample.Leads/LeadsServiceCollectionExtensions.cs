using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace UmbracoSample.Leads;

public static class LeadsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="MapClient"/>, bound to the <c>Leads:Map</c> section and validated at startup. The client opts
    /// out of the standard resilience handler that ServiceDefaults adds to every client, because it owns its retries
    /// (threat model 0001, retry amplification), and it sends no trace context, so a lead cannot be joined to the site's
    /// telemetry through the MAP (threat model 0001, repudiation).
    /// </summary>
    public static IServiceCollection AddMapClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MapOptions>()
            .Bind(configuration.GetSection(MapOptions.Section))
            .ValidateDataAnnotations()
            .Validate(o => o.Validate() is null, "Leads:Map is invalid; see MapOptions.Validate.")
            .ValidateOnStart();
        services.AddSingleton<MapBulkhead>();
        services.AddSingleton(TimeProvider.System);
        var client = services.AddHttpClient<MapClient>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<MapOptions>>().Value;
            http.BaseAddress = EnsureTrailingSlash(options.BaseAddress!);

            // The client enforces its own, shorter timeouts; this only stops a call from outliving the budget.
            http.Timeout = options.Budget + TimeSpan.FromSeconds(1);
        });
        client.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            ActivityHeadersPropagator = DistributedContextPropagator.CreateNoOutputPropagator(),
        });

        // Experimental in this package version. Without it, the standard handler from ServiceDefaults would retry inside
        // each attempt and multiply the calls; a contract test counts them.
#pragma warning disable EXTEXP0001
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        return services;
    }

    private static Uri EnsureTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri(address.AbsoluteUri + "/");
}

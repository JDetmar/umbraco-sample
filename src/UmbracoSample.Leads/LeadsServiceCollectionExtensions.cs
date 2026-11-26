using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.TryAddSingleton(TimeProvider.System);
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
        services.AddTransient<IMapClient>(provider => provider.GetRequiredService<MapClient>());
        return services;
    }

    /// <summary>
    /// Registers the lead form (threat model 0001): its options, the render token protector, the rate-limit policy
    /// <see cref="LeadFormRateLimitPolicy.Name"/> and the submission handler, plus the MAP client. The host supplies an
    /// <see cref="ILeadFormCatalog"/>, maps its endpoint with antiforgery, the 16 KB body limit and the rate-limit policy,
    /// and calls <c>UseRateLimiter</c>. With the CAPTCHA switched on and no <see cref="ICaptchaVerifier"/> registered, the
    /// options fail validation and the site does not start (D1: fail closed).
    /// </summary>
    public static IServiceCollection AddLeadForm(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMapClient(configuration);
        services.AddOptions<LeadFormOptions>().Bind(configuration.GetSection(LeadFormOptions.Section)).ValidateDataAnnotations()
            .Validate(o => o.Window > TimeSpan.Zero, "Leads:Form:Window must be positive.").ValidateOnStart();
        services.AddOptions<EdgeOptions>().Bind(configuration.GetSection(EdgeOptions.Section));
        services.AddOptions<CaptchaOptions>().Bind(configuration.GetSection(CaptchaOptions.Section));
        services.AddSingleton<IValidateOptions<CaptchaOptions>, CaptchaNeedsAVerifier>();
        services.AddOptions<CaptchaOptions>().ValidateOnStart();
        services.AddDataProtection();
        services.AddSingleton<RenderTokenProtector>();
        services.AddScoped<LeadSubmissionHandler>();
        services.AddRateLimiter(limiter => limiter.AddPolicy<string, LeadFormRateLimitPolicy>(LeadFormRateLimitPolicy.Name));
        return services;
    }

    private sealed class CaptchaNeedsAVerifier(IEnumerable<ICaptchaVerifier> verifiers) : IValidateOptions<CaptchaOptions>
    {
        public ValidateOptionsResult Validate(string? name, CaptchaOptions options) =>
            options.Enabled && !verifiers.Any()
                ? ValidateOptionsResult.Fail("Leads:Captcha:Enabled is on, but no CAPTCHA verifier is registered (threat model 0001, D1).")
                : ValidateOptionsResult.Success;
    }

    private static Uri EnsureTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri(address.AbsoluteUri + "/");
}

using UmbracoSample.Leads;
using UmbracoSample.Web.Storage;

namespace UmbracoSample.Web.Leads;

public static class LeadSiteServiceCollectionExtensions
{
    public const string OutputCacheSetting = "Umbraco:CMS:Website:OutputCache:Enabled";

    /// <summary>
    /// Registers the lead form for this site. Its options are always bound and validated; the form's services (the MAP
    /// client, the render token, the rate-limit policy and the handler) only when <c>Leads:Enabled</c> is on, so a site
    /// with the form off needs no MAP configuration. Outside development the form also needs the shared key ring
    /// (<see cref="KeyRing"/>), because a render token must read on every replica and after a new revision (threat model
    /// 0001, R3).
    /// </summary>
    public static IServiceCollection AddLeadSite(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        // Umbraco's website output cache would serve every visitor the same render token, and so the same idempotency
        // key, merging their leads (threat model 0001, section 5). It ignores Cache-Control set while rendering, so the
        // form refuses to start alongside it.
        var outputCache = configuration.GetValue<bool>(OutputCacheSetting);
        var sharedKeys = environment.IsDevelopment() || KeyRing.IsComplete(configuration);
        services.AddOptions<LeadSiteOptions>()
            .Bind(configuration.GetSection(LeadSiteOptions.Section))
            .Validate(o => o.Validate() is null, "Leads is invalid; see LeadSiteOptions.Validate.")
            .Validate(o => !(o.Enabled && outputCache), $"Leads:Enabled cannot be on while {OutputCacheSetting} is on (threat model 0001, section 5).")
            .Validate(o => !o.Enabled || sharedKeys, $"Leads:Enabled needs the shared key ring ({KeyRing.BlobUriKey} and {KeyRing.KeyUriKey}) outside development (threat model 0001, R3).")
            .ValidateOnStart();
        services.AddHttpContextAccessor();
        services.AddScoped<LeadFormView>();

        if (configuration.GetValue<bool>($"{LeadSiteOptions.Section}:{nameof(LeadSiteOptions.Enabled)}"))
        {
            services.AddLeadForm(configuration);
            services.AddScoped<ILeadFormCatalog, PublishedLeadFormCatalog>();
        }

        return services;
    }
}

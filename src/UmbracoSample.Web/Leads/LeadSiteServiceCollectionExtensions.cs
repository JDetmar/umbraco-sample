using UmbracoSample.Leads;

namespace UmbracoSample.Web.Leads;

public static class LeadSiteServiceCollectionExtensions
{
    public const string OutputCacheSetting = "Umbraco:CMS:Website:OutputCache:Enabled";

    /// <summary>
    /// Registers the lead form for this site. Its options are always bound and validated; the form's services (the MAP
    /// client, the render token, the rate-limit policy and the handler) only when <c>Leads:Enabled</c> is on, so a site
    /// with the form off needs no MAP configuration.
    /// </summary>
    public static IServiceCollection AddLeadSite(this IServiceCollection services, IConfiguration configuration)
    {
        // Umbraco's website output cache would serve every visitor the same render token, and so the same idempotency
        // key, merging their leads (threat model 0001, section 5). It ignores Cache-Control set while rendering, so the
        // form refuses to start alongside it.
        var outputCache = configuration.GetValue<bool>(OutputCacheSetting);
        services.AddOptions<LeadSiteOptions>()
            .Bind(configuration.GetSection(LeadSiteOptions.Section))
            .Validate(o => o.Validate() is null, "Leads is invalid; see LeadSiteOptions.Validate.")
            .Validate(o => !(o.Enabled && outputCache), $"Leads:Enabled cannot be on while {OutputCacheSetting} is on (threat model 0001, section 5).")
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

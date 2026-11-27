using UmbracoSample.Leads;

namespace UmbracoSample.Web.Leads;

public static class LeadSiteServiceCollectionExtensions
{
    /// <summary>
    /// Registers the lead form for this site. Its options are always bound and validated; the form's services (the MAP
    /// client, the render token, the rate-limit policy and the handler) only when <c>Leads:Enabled</c> is on, so a site
    /// with the form off needs no MAP configuration.
    /// </summary>
    public static IServiceCollection AddLeadSite(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LeadSiteOptions>()
            .Bind(configuration.GetSection(LeadSiteOptions.Section))
            .Validate(o => o.Validate() is null, "Leads is invalid; see LeadSiteOptions.Validate.")
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

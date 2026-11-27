using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Shouldly;
using UmbracoSample.Components;
using UmbracoSample.Web.Leads;
using UmbracoSample.Web.Storage;

namespace UmbracoSample.Web.Tests.Leads;

/// <summary>
/// The configuration every deployed environment runs today: the form off (threat model 0001, R2 and R3). A page with a
/// Form block must still render, without any of the lead services, which are not registered.
/// </summary>
public sealed class LeadFormOffTests
{
    private static readonly FormModel Form = new(Guid.NewGuid(), "Contact us", null, "general-enquiry", "2026-11", "Thanks");

    private static ServiceProvider Site(params (string Key, string Value)[] settings) => Site(Environments.Development, settings);

    private static ServiceProvider Site(string environment, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLeadSite(configuration, new HostingEnvironment { EnvironmentName = environment });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void With_the_form_off_a_form_block_renders_nothing_and_needs_no_lead_services()
    {
        using var site = Site(("Leads:Consent:2026-11", "Yes, email me."));
        using var scope = site.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();

        scope.ServiceProvider.GetRequiredService<LeadFormView>().For(Form).ShouldBeNull();
    }

    [Fact]
    public void The_form_refuses_to_start_alongside_the_website_output_cache()
    {
        using var site = Site(("Leads:Enabled", "true"), (LeadSiteServiceCollectionExtensions.OutputCacheSetting, "true"));

        Should.Throw<OptionsValidationException>(() => site.GetRequiredService<IOptions<LeadSiteOptions>>().Value);
    }

    [Fact]
    public void A_version_with_no_wording_renders_no_form()
    {
        using var site = Site(("Leads:Consent:2025-01", "Old wording."));
        using var scope = site.CreateScope();

        scope.ServiceProvider.GetRequiredService<LeadFormView>().For(Form).ShouldBeNull();
    }

    [Fact]
    public void Outside_development_the_form_refuses_to_start_without_the_shared_key_ring()
    {
        using var site = Site(Environments.Production, ("Leads:Enabled", "true"));

        Should.Throw<OptionsValidationException>(() => site.GetRequiredService<IOptions<LeadSiteOptions>>().Value)
            .Message.ShouldContain("shared key ring");
    }

    [Theory]
    [InlineData(KeyRing.BlobUriKey, "https://account.blob.core.windows.net/dataprotection/keys.xml")]
    [InlineData(KeyRing.KeyUriKey, "https://vault.vault.azure.net/keys/dataprotection")]
    public void Outside_development_half_a_key_ring_does_not_satisfy_the_form(string key, string value)
    {
        using var site = Site(Environments.Production, ("Leads:Enabled", "true"), (key, value));

        Should.Throw<OptionsValidationException>(() => site.GetRequiredService<IOptions<LeadSiteOptions>>().Value)
            .Message.ShouldContain("shared key ring");
    }

    [Fact]
    public void Outside_development_the_shared_key_ring_satisfies_the_form()
    {
        using var site = Site(
            Environments.Production,
            ("Leads:Enabled", "true"),
            (KeyRing.BlobUriKey, "https://account.blob.core.windows.net/dataprotection/keys.xml"),
            (KeyRing.KeyUriKey, "https://vault.vault.azure.net/keys/dataprotection"));

        site.GetRequiredService<IOptions<LeadSiteOptions>>().Value.Enabled.ShouldBeTrue();
    }
}

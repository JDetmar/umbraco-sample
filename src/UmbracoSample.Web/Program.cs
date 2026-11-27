using UmbracoSample.Security;
using UmbracoSample.Web.Health;
using UmbracoSample.Web.Leads;
using UmbracoSample.Web.Redirects;
using UmbracoSample.Web.Security;
using UmbracoSample.Web.Storage;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Do not advertise the server software (TST-09).
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .AddMediaStorage(builder.Configuration)
    .Build();

builder.Services.AddLeadSite(builder.Configuration);

// Parse the redirect maps before booting, so an invalid map stops the site (ES-021).
var redirects = RedirectMiddleware.Load(builder.Environment.ContentRootPath);

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseSecurityHeaders(ThirdPartyAllowlist.Origins);
app.UseRedirectMap(redirects);
app.UseFrontEndBodyLimit();

app.MapDefaultEndpoints();
app.MapSchemaDriftEndpoint();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();

        // After routing, so the lead form's endpoint metadata selects the "lead-form" policy; endpoints without a
        // policy are not limited.
        // The limiter's services exist only when the form is on.
        if (builder.Configuration.GetValue<bool>($"{LeadSiteOptions.Section}:{nameof(LeadSiteOptions.Enabled)}"))
        {
            u.AppBuilder.UseRateLimiter();
        }
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();

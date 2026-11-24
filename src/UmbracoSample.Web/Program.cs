using UmbracoSample.Web.Health;
using UmbracoSample.Web.Redirects;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

// Parse the redirect map before booting, so an invalid map stops the site (ES-021).
var redirects = RedirectMiddleware.Load(builder.Environment.ContentRootPath);

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseRedirectMap(redirects);

app.MapDefaultEndpoints();
app.MapSchemaDriftEndpoint();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();

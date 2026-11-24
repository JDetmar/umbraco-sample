using UmbracoSample.Web.Health;
using UmbracoSample.Web.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Do not advertise the server software (TST-09).
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseSecurityHeaders();

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

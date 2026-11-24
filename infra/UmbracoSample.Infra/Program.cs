using Pulumi;
using UmbracoSample.Infra;

return await Deployment.RunAsync(() =>
{
    var settings = SiteSettings.From(new Config());
    return new SiteInfrastructure(settings).Outputs;
});

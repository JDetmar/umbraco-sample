using Pulumi;
using UmbracoSample.Infra;

return await Deployment.RunAsync(() =>
{
    var settings = SiteSettings.From(new Config());
    var config = new Config();
    return new SiteInfrastructure(settings, SiteSecrets.From(config)).Outputs;
});

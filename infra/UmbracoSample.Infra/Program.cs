using Pulumi;
using UmbracoSample.Infra;

return await Deployment.RunAsync(() =>
{
    var config = new Config();
    var settings = SiteSettings.From(config);
    return new SiteInfrastructure(settings, SiteSecrets.From(config)).Outputs;
});

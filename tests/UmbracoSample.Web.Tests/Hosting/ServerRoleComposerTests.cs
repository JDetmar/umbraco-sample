using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Umbraco.Cms.Core.Sync;
using UmbracoSample.Web.Hosting;

namespace UmbracoSample.Web.Tests.Hosting;

public sealed class ServerRoleComposerTests
{
    [Fact]
    public void The_composer_replaces_umbracos_elected_role_with_the_single_server_role()
    {
        // Umbraco registers the elected accessor when the builder is created, before composers run.
        var builder = new ServicesOnlyUmbracoBuilder();
        builder.Services.AddSingleton<IServerRoleAccessor, ElectedServerRoleAccessor>();

        new ServerRoleComposer().Compose(builder);

        var registration = builder.Services.Where(d => d.ServiceType == typeof(IServerRoleAccessor)).ShouldHaveSingleItem();
        registration.ImplementationType.ShouldBe(typeof(SingleServerRoleAccessor));
    }
}

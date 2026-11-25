using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Umbraco.Cms.Core.Sync;
using UmbracoSample.Web.Hosting;

namespace UmbracoSample.Web.Tests.Hosting;

public sealed class ServerRoleComposerTests
{
    [Fact]
    public void The_site_is_always_the_single_umbraco_server()
    {
        var builder = new ServicesOnlyUmbracoBuilder();

        new ServerRoleComposer().Compose(builder);

        using var provider = builder.Services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IServerRoleAccessor>();
        accessor.ShouldBeOfType<SingleServerRoleAccessor>();
        accessor.CurrentServerRole.ShouldBe(ServerRole.Single);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using UmbracoSample.Web.Governance;

namespace UmbracoSample.Web.Tests.Governance;

public sealed class GovernanceComposerTests
{
    [Fact]
    public void The_health_check_gets_the_system_clock()
    {
        var builder = new ServicesOnlyUmbracoBuilder();

        new GovernanceComposer().Compose(builder);

        builder.Services.BuildServiceProvider().GetRequiredService<TimeProvider>().ShouldBe(TimeProvider.System);
    }
}

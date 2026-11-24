using Shouldly;

namespace UmbracoSample.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class WalkingSkeletonTests(SiteFixture site)
{
    [Fact]
    public async Task Home_page_renders_seed_content_imported_from_uSync()
    {
        var html = await site.Web.GetStringAsync("/", TestContext.Current.CancellationToken);

        html.ShouldContain("<h1>Umbraco sample</h1>");
        html.ShouldContain("<html lang=\"en\">");
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    public async Task Health_probes_report_healthy_without_details(string path)
    {
        using var response = await site.Web.GetAsync(path, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue();
        body.ShouldBe("Healthy");
    }

    [Fact]
    public async Task Schema_matches_the_uSync_files_after_first_boot()
    {
        using var response = await site.Web.GetAsync("/health/schema", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldBe("Healthy", "ENV-06: the database schema must match the uSync files in source control.");
    }

    [Fact]
    public async Task Back_office_is_served()
    {
        using var response = await site.Web.GetAsync("/umbraco", TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue();
    }
}

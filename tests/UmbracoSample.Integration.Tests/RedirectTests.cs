using Shouldly;
using UmbracoSample.Domain.Redirects;

namespace UmbracoSample.Integration.Tests;

/// <summary>
/// TST-08: every rule in the committed redirect map answers with its status and location on the real stack, and every
/// on-site target is a live page. External targets are checked for shape only; this test does not call other sites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RedirectTests(SiteFixture site)
{
    // Reserved prefixes are checked by the Web unit test of the committed map; here only the rules matter.
    private static readonly RedirectMap Map = RedirectMap.Parse(
        File.ReadAllText(Path.Join(AppContext.BaseDirectory, "redirects.csv"))).Map
        ?? throw new InvalidOperationException("redirects.csv is invalid; see the Web unit tests.");

    public static TheoryData<string> Sources => [.. Map.Rules.Select(rule => rule.Source.Value)];

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task Every_old_address_redirects_once_to_a_live_page(string source)
    {
        var rule = Map.Find(source)!;
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = site.Web.BaseAddress, Timeout = TimeSpan.FromSeconds(60) };

        using var redirect = await client.GetAsync(source + "?from=test", TestContext.Current.CancellationToken);

        ((int)redirect.StatusCode).ShouldBe((int)rule.Kind);
        var location = redirect.Headers.Location!.OriginalString;
        location.ShouldBe(rule.Target.LocationFor("?from=test"), "the request query is kept unless the target sets its own");
        redirect.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"], "TST-09: redirect responses carry the security headers too");

        if (rule.Target.SitePath is null)
        {
            return;
        }

        using var target = await client.GetAsync(location, TestContext.Current.CancellationToken);
        ((int)target.StatusCode).ShouldBe(200, $"{source} redirects to {location}, which must be a live page in one hop");
    }
}

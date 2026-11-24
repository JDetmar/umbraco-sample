using System.Text.RegularExpressions;
using Shouldly;

namespace UmbracoSample.Integration.Tests;

/// <summary>
/// Renders the seeded component fixtures through the real stack (TST-05): empty, minimal and maximal content
/// for every component, plus the page-level accessibility rules the components promise (CMP-04, ES-035).
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class ComponentFixtureTests(SiteFixture site)
{
    private static readonly string[] ComponentRoots = ["c-hero", "c-rich-text", "c-card-grid", "c-cta"];

    [Fact]
    public async Task Empty_fixture_renders_only_the_page_heading()
    {
        var main = await MainAsync("/fixture-empty/");

        main.ShouldContain("<h1>Component fixture: empty</h1>");
        foreach (var root in ComponentRoots)
        {
            main.ShouldNotContain($"class=\"{root}\"");
        }
    }

    [Fact]
    public async Task Minimal_fixture_renders_every_component_with_required_fields_only()
    {
        var main = await MainAsync("/fixture-minimal/");

        foreach (var root in ComponentRoots)
        {
            main.ShouldContain($"class=\"{root}\"");
        }

        main.ShouldContain("<h2 class=\"c-hero__heading\">Minimal hero</h2>");
        main.ShouldNotContain("c-hero__intro");
        main.ShouldNotContain("c-hero__image");
        main.ShouldContain("<p>Minimal rich text.</p>");
        main.ShouldNotContain("c-card-grid__heading");
        main.ShouldContain("<h2 class=\"c-card__title\">", customMessage: "without a grid heading, card titles are level 2");
        main.ShouldContain("<a href=\"/\">Minimal link</a>");
    }

    [Fact]
    public async Task Maximal_fixture_renders_every_field()
    {
        var main = await MainAsync("/fixture-maximal/");

        main.ShouldContain("c-hero__intro");
        main.ShouldContain("<h3>Third-level heading</h3>");
        main.ShouldContain("<blockquote>");
        main.ShouldContain("<h2 class=\"c-card-grid__heading\">Maximal card grid</h2>");
        CountOf(main, "class=\"c-card\"").ShouldBe(3);
        main.ShouldContain("<h3 class=\"c-card__title\">");
        main.ShouldContain("<p class=\"c-cta__text\">Supporting text for the call to action.</p>");
    }

    [Fact]
    public async Task Links_that_open_a_new_tab_say_so_and_cannot_reach_back()
    {
        var main = await MainAsync("/fixture-maximal/");

        var newTabLinks = NewTabLink().Matches(main);
        newTabLinks.Count.ShouldBeGreaterThan(0);
        foreach (Match link in newTabLinks)
        {
            link.Value.ShouldContain("rel=\"noopener noreferrer\"");
            link.Value.ShouldContain("(opens in a new tab)");
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/fixture-empty/")]
    [InlineData("/fixture-minimal/")]
    [InlineData("/fixture-maximal/")]
    public async Task Every_page_has_one_h1_and_never_skips_a_heading_level(string path)
    {
        var html = await site.Web.GetStringAsync(path, TestContext.Current.CancellationToken);
        var levels = HeadingTag().Matches(html).Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();

        levels.Count(level => level == 1).ShouldBe(1);
        levels[0].ShouldBe(1);
        for (var i = 1; i < levels.Count; i++)
        {
            (levels[i] - levels[i - 1]).ShouldBeLessThanOrEqualTo(1, $"{path}: h{levels[i - 1]} is followed by h{levels[i]}");
        }
    }

    private async Task<string> MainAsync(string path)
    {
        var html = await site.Web.GetStringAsync(path, TestContext.Current.CancellationToken);
        var start = html.IndexOf("<main", StringComparison.Ordinal);
        var end = html.IndexOf("</main>", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        return html[start..end];
    }

    private static int CountOf(string text, string value) => Regex.Count(text, Regex.Escape(value));

    [GeneratedRegex("<h([1-6])[\\s>]")]
    private static partial Regex HeadingTag();

    [GeneratedRegex("<a [^>]*target=\"_blank\"[^>]*>.*?</a>", RegexOptions.Singleline)]
    private static partial Regex NewTabLink();
}

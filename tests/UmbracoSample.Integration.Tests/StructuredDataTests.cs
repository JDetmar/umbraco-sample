using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;

namespace UmbracoSample.Integration.Tests;

/// <summary>Every page carries one JSON-LD block that describes it at its real address (ARC-08).</summary>
[Trait("Category", "Integration")]
public sealed partial class StructuredDataTests(SiteFixture site)
{
    public static TheoryData<string> Pages => ["/", "/fixture-empty/", "/fixture-minimal/", "/fixture-maximal/"];

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Each_page_describes_itself_as_a_web_page_in_the_site(string path)
    {
        var html = await site.Web.GetStringAsync(path, TestContext.Current.CancellationToken);

        var block = JsonLd().Matches(html).ShouldHaveSingleItem();
        using var json = JsonDocument.Parse(block.Groups["json"].Value);
        var graph = json.RootElement.GetProperty("@graph").EnumerateArray().ToList();
        var page = graph.Single(node => node.GetProperty("@type").GetString() == "WebPage");
        var webSite = graph.Single(node => node.GetProperty("@type").GetString() == "WebSite");

        new Uri(page.GetProperty("url").GetString()!).AbsolutePath.ShouldBe(path);
        page.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        page.GetProperty("isPartOf").GetProperty("@id").GetString().ShouldBe(webSite.GetProperty("@id").GetString());
        new Uri(webSite.GetProperty("url").GetString()!).AbsolutePath.ShouldBe("/");

        page.GetProperty("@id").GetString().ShouldNotBe(webSite.GetProperty("@id").GetString());
        var modified = page.GetProperty("dateModified").GetString()!;
        modified.ShouldEndWith("Z");
        DateTimeOffset.Parse(modified, System.Globalization.CultureInfo.InvariantCulture).ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddMinutes(5));

        if (path == "/")
        {
            page.TryGetProperty("breadcrumb", out _).ShouldBeFalse();
        }
        else
        {
            var trail = page.GetProperty("breadcrumb").GetProperty("itemListElement").EnumerateArray().ToList();
            trail.Select(item => item.GetProperty("position").GetInt32()).ShouldBe(Enumerable.Range(1, trail.Count));
            trail[0].GetProperty("item").GetString().ShouldBe(webSite.GetProperty("url").GetString());
            trail[^1].GetProperty("item").GetString().ShouldBe(page.GetProperty("url").GetString());
        }
    }

    [GeneratedRegex("<script type=\"application/ld\\+json\">(?<json>.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLd();
}

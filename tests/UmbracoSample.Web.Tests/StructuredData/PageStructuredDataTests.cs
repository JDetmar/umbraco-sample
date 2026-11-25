using System.Text.Json;
using Shouldly;
using UmbracoSample.Web.StructuredData;

namespace UmbracoSample.Web.Tests.StructuredData;

public sealed class PageStructuredDataTests
{
    private static readonly Uri Root = new("https://site.example/");
    private static readonly Uri About = new("https://site.example/about/");
    private static readonly DateTimeOffset Published = new(2026, 11, 25, 9, 30, 0, TimeSpan.FromHours(-6));

    private static JsonElement Graph(string json, string type) =>
        JsonDocument.Parse(json).RootElement.GetProperty("@graph").EnumerateArray().Single(node => node.GetProperty("@type").GetString() == type);

    [Fact]
    public void A_page_is_a_web_page_inside_its_web_site()
    {
        var json = PageStructuredData.Build(new PageFacts("About", "Who we are", About, Published, [("Home", Root), ("About", About)]));

        JsonDocument.Parse(json).RootElement.GetProperty("@context").GetString().ShouldBe("https://schema.org");
        var page = Graph(json, "WebPage");
        page.GetProperty("url").GetString().ShouldBe("https://site.example/about/");
        page.GetProperty("name").GetString().ShouldBe("About");
        page.GetProperty("description").GetString().ShouldBe("Who we are");
        page.GetProperty("dateModified").GetString().ShouldBe("2026-11-25T15:30:00Z");
        page.GetProperty("isPartOf").GetProperty("@id").GetString().ShouldBe("https://site.example/#website");
        Graph(json, "WebSite").GetProperty("name").GetString().ShouldBe("Home");
    }

    [Fact]
    public void The_breadcrumb_lists_every_level_in_order()
    {
        var deep = new Uri("https://site.example/about/team/");
        var json = PageStructuredData.Build(new PageFacts("Team", null, deep, Published, [("Home", Root), ("About", About), ("Team", deep)]));

        var items = Graph(json, "WebPage").GetProperty("breadcrumb").GetProperty("itemListElement").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("position").GetInt32()).ShouldBe([1, 2, 3]);
        items.Select(item => item.GetProperty("item").GetString()).ShouldBe(["https://site.example/", "https://site.example/about/", "https://site.example/about/team/"]);
    }

    [Fact]
    public void The_home_page_has_no_breadcrumb_and_no_empty_description()
    {
        var json = PageStructuredData.Build(new PageFacts("Home", "  ", Root, Published, [("Home", Root)]));

        var page = Graph(json, "WebPage");
        page.TryGetProperty("breadcrumb", out _).ShouldBeFalse();
        page.TryGetProperty("description", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("</script><script>alert(1)</script>")]
    [InlineData("<!-- x")]
    [InlineData("a & b \"quoted\" 'single'")]
    public void Editor_text_cannot_close_the_script_block_or_open_markup(string hostile)
    {
        var json = PageStructuredData.Build(new PageFacts(hostile, hostile, About, Published, [(hostile, Root), (hostile, About)]));

        json.ShouldNotContain("<");
        json.ShouldNotContain(">");
        json.ShouldNotContain("&");
        Graph(json, "WebPage").GetProperty("name").GetString().ShouldBe(hostile);
    }

    [Fact]
    public void On_the_home_page_the_site_and_the_page_are_two_nodes()
    {
        var json = PageStructuredData.Build(new PageFacts("Home", null, Root, Published, [("Home", Root)]));

        Graph(json, "WebSite").GetProperty("@id").GetString().ShouldNotBe(Graph(json, "WebPage").GetProperty("@id").GetString());
        Graph(json, "WebPage").GetProperty("isPartOf").GetProperty("@id").GetString().ShouldBe(Graph(json, "WebSite").GetProperty("@id").GetString());
    }

    [Fact]
    public void An_unroutable_page_gets_no_structured_data_and_no_exception() =>
        PageStructuredData.TryBuild("Orphan", null, "#", Published, [("Home", "https://site.example/"), ("Orphan", "#")]).ShouldBeNull();

    [Fact]
    public void An_unroutable_ancestor_is_left_out_of_the_breadcrumb()
    {
        var deep = "https://site.example/about/team/";
        var json = PageStructuredData.TryBuild("Team", null, deep, Published, [("Home", "https://site.example/"), ("About", "#"), ("Team", deep)]);

        var items = Graph(json!, "WebPage").GetProperty("breadcrumb").GetProperty("itemListElement").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("item").GetString()).ShouldBe(["https://site.example/", deep]);
        items.Select(item => item.GetProperty("position").GetInt32()).ShouldBe([1, 2]);
    }

    [Fact]
    public void A_trail_that_does_not_end_at_the_page_is_refused() =>
        Should.Throw<ArgumentException>(() => PageStructuredData.Build(new PageFacts("About", null, About, Published, [("Home", Root)])));
}

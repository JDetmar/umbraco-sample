using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UmbracoSample.Web.StructuredData;

/// <summary>A page as structured data sees it: its own name, address and place in the site.</summary>
/// <param name="Name">The page title.</param>
/// <param name="Description">The meta description, if the page has one.</param>
/// <param name="Url">The page's absolute canonical URL.</param>
/// <param name="Modified">When the published version was last updated.</param>
/// <param name="Trail">Every page from the site root down to this one, this one last.</param>
public sealed record PageFacts(string Name, string? Description, Uri Url, DateTimeOffset Modified, IReadOnlyList<(string Name, Uri Url)> Trail);

/// <summary>
/// Schema.org JSON-LD for a page (ARC-08): a WebPage inside its WebSite, with a breadcrumb trail, so search engines
/// and AI readers get the title, summary, address and hierarchy without parsing markup. The output is embedded in a
/// <c>&lt;script type="application/ld+json"&gt;</c> block, which browsers never execute; the content security policy
/// is unchanged (ADR 0008).
/// </summary>
public static class PageStructuredData
{
    // The default encoder escapes <, >, & and quotes as \u sequences, so editor text can never close the script
    // block or open markup, whatever it contains.
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.Default };

    /// <summary>
    /// Builds the JSON-LD from URLs as Umbraco renders them, or returns null if the page itself has no absolute URL
    /// (Umbraco renders "#" for content it cannot route). Unroutable ancestors are left out of the breadcrumb. Never
    /// throws for such content, so structured data can never take a page down.
    /// </summary>
    public static string? TryBuild(string name, string? description, string pageUrl, DateTimeOffset modified, IEnumerable<(string Name, string Url)> rootToPage)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var url))
        {
            return null;
        }

        var trail = new List<(string Name, Uri Url)>();
        foreach (var (itemName, itemUrl) in rootToPage)
        {
            if (Uri.TryCreate(itemUrl, UriKind.Absolute, out var item) && item != url)
            {
                trail.Add((itemName, item));
            }
        }

        trail.Add((name, url));
        return Build(new PageFacts(name, description, url, modified, trail));
    }

    public static string Build(PageFacts page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Trail.Count == 0 || page.Trail[^1].Url != page.Url)
        {
            throw new ArgumentException("The trail must end with the page itself.", nameof(page));
        }

        var site = page.Trail[0];
        var webPage = new JsonObject
        {
            ["@type"] = "WebPage",
            ["@id"] = page.Url.AbsoluteUri,
            ["url"] = page.Url.AbsoluteUri,
            ["name"] = page.Name,
            ["inLanguage"] = "en",
            ["dateModified"] = page.Modified.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            ["isPartOf"] = new JsonObject { ["@id"] = SiteId(site.Url) },
        };
        if (!string.IsNullOrWhiteSpace(page.Description))
        {
            webPage["description"] = page.Description;
        }

        var graph = new JsonArray
        {
            new JsonObject
            {
                ["@type"] = "WebSite",
                // Its own identifier, so on the home page the site and the page stay two nodes.
                ["@id"] = SiteId(site.Url),
                ["url"] = site.Url.AbsoluteUri,
                ["name"] = site.Name,
            },
            webPage,
        };

        if (page.Trail.Count > 1)
        {
            var items = new JsonArray();
            for (var i = 0; i < page.Trail.Count; i++)
            {
                items.Add(new JsonObject
                {
                    ["@type"] = "ListItem",
                    ["position"] = i + 1,
                    ["name"] = page.Trail[i].Name,
                    ["item"] = page.Trail[i].Url.AbsoluteUri,
                });
            }

            webPage["breadcrumb"] = new JsonObject { ["@type"] = "BreadcrumbList", ["itemListElement"] = items };
        }

        return new JsonObject { ["@context"] = "https://schema.org", ["@graph"] = graph }.ToJsonString(Options);
    }

    private static string SiteId(Uri root) => $"{root.AbsoluteUri}#website";
}

using System.Net;
using System.Text.RegularExpressions;

namespace UmbracoSample.Tests.Support;

/// <summary>A URL a page makes the browser fetch or submit to, and the policy directive that governs it.</summary>
public sealed record ResourceReference(string Directive, string Url, string Source);

/// <summary>
/// Finds everything a page or stylesheet makes the browser load, and checks each against a content security policy
/// (TST-10). Shared by the fast unit tests, which prove it catches hostile markup, and the integration tests, which
/// run it on every seeded page. Navigation links (a href, canonical, alternate) load nothing and are not reported.
/// </summary>
public static partial class ResourceReferences
{
    private static readonly HashSet<string> NonLoadingLinkRels = new(StringComparer.OrdinalIgnoreCase)
    {
        "canonical", "alternate", "author", "license", "next", "prev", "me", "help", "search", "bookmark", "tag",
        "nofollow", "noopener", "noreferrer", "external",
    };

    /// <summary>Every fetch or submission in the markup, with the directive that governs it.</summary>
    public static IReadOnlyList<ResourceReference> InHtml(string html)
    {
        var found = new List<ResourceReference>();
        foreach (Match tag in StartTag().Matches(html))
        {
            var name = tag.Groups["name"].Value.ToLowerInvariant();
            var attributes = Attributes(tag.Groups["attrs"].Value);
            string? Attr(string key) => attributes.TryGetValue(key, out var value) ? value : null;
            void Add(string directive, string? url)
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    found.Add(new ResourceReference(directive, url.Trim(), $"<{name}>"));
                }
            }

            void AddSrcset(string directive, string? srcset)
            {
                foreach (var candidate in (srcset ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    Add(directive, candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
                }
            }

            switch (name)
            {
                case "script":
                    Add("script-src", Attr("src"));
                    break;
                case "img":
                    Add("img-src", Attr("src"));
                    AddSrcset("img-src", Attr("srcset"));
                    break;
                case "source":
                    Add("media-src", Attr("src"));
                    AddSrcset("img-src", Attr("srcset"));
                    break;
                case "video":
                    Add("media-src", Attr("src"));
                    Add("img-src", Attr("poster"));
                    break;
                case "audio" or "track":
                    Add("media-src", Attr("src"));
                    break;
                case "input":
                    if (string.Equals(Attr("type"), "image", StringComparison.OrdinalIgnoreCase))
                    {
                        Add("img-src", Attr("src"));
                    }

                    Add("form-action", Attr("formaction"));
                    break;
                case "button":
                    Add("form-action", Attr("formaction"));
                    break;
                case "form":
                    Add("form-action", Attr("action"));
                    break;
                case "iframe" or "frame":
                    Add("frame-src", Attr("src"));
                    break;
                case "object":
                    Add("object-src", Attr("data"));
                    break;
                case "embed":
                    Add("object-src", Attr("src"));
                    break;
                case "base":
                    Add("base-uri", Attr("href"));
                    break;
                case "meta":
                    if (string.Equals(Attr("http-equiv"), "refresh", StringComparison.OrdinalIgnoreCase))
                    {
                        var target = RefreshUrl().Match(Attr("content") ?? string.Empty);
                        Add("default-src", target.Success ? target.Groups["url"].Value : null);
                    }

                    break;
                case "link":
                    AddLink(Attr("rel"), Attr("as"), Attr("href"), Attr("imagesrcset"), Add, AddSrcset);
                    break;
            }
        }

        return found;
    }

    /// <summary>Every url() and @import in a stylesheet; urls inside @font-face are fonts, the rest images.</summary>
    public static IReadOnlyList<ResourceReference> InCss(string css)
    {
        var found = new List<ResourceReference>();
        var fontFaces = FontFace().Matches(css).Select(match => (match.Index, End: match.Index + match.Length)).ToList();
        foreach (Match import in CssImport().Matches(css))
        {
            found.Add(new ResourceReference("style-src", import.Groups["url"].Value.Trim(), "@import"));
        }

        foreach (Match url in CssUrl().Matches(css))
        {
            if (CssImport().Matches(css).Any(import => url.Index >= import.Index && url.Index < import.Index + import.Length))
            {
                continue;
            }

            var inFontFace = fontFaces.Any(block => url.Index >= block.Index && url.Index < block.End);
            found.Add(new ResourceReference(inFontFace ? "font-src" : "img-src", url.Groups["url"].Value.Trim(), "url()"));
        }

        return found;
    }

    /// <summary>The references the policy would block, given the page's own URL.</summary>
    public static IReadOnlyList<ResourceReference> NotAllowed(IEnumerable<ResourceReference> references, string policy, Uri page)
    {
        var directives = policy
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(part => part[0], part => part.Skip(1).ToList(), StringComparer.OrdinalIgnoreCase);

        return references.Where(reference => !Allowed(reference, directives, page)).ToList();
    }

    private static bool Allowed(ResourceReference reference, Dictionary<string, List<string>> directives, Uri page)
    {
        // form-action and base-uri do not fall back to default-src; a missing one allows everything, which the policy never does.
        if (!directives.TryGetValue(reference.Directive, out var sources)
            && (reference.Directive is "form-action" or "base-uri" || !directives.TryGetValue("default-src", out sources)))
        {
            return reference.Directive is "form-action" or "base-uri";
        }

        if (reference.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return sources.Contains("data:");
        }

        if (!Uri.TryCreate(page, reference.Url, out var resolved))
        {
            return false;
        }

        if (resolved.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var origin = resolved.GetLeftPart(UriPartial.Authority);
        var sameOrigin = string.Equals(origin, page.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
        return (sameOrigin && sources.Contains("'self'")) || sources.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddLink(string? rel, string? asType, string? href, string? imageSrcset, Action<string, string?> add, Action<string, string?> addSrcset)
    {
        var rels = (rel ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (rels.Length > 0 && rels.All(NonLoadingLinkRels.Contains))
        {
            return;
        }

        foreach (var token in rels.Select(token => token.ToLowerInvariant()))
        {
            switch (token)
            {
                case "stylesheet":
                    add("style-src", href);
                    break;
                case "icon" or "apple-touch-icon" or "shortcut" or "mask-icon":
                    add("img-src", href);
                    break;
                case "manifest":
                    add("manifest-src", href);
                    break;
                case "modulepreload":
                    add("script-src", href);
                    break;
                case "preload" or "prefetch":
                    var directive = (asType ?? string.Empty).ToLowerInvariant() switch
                    {
                        "script" or "worker" => "script-src",
                        "style" => "style-src",
                        "image" => "img-src",
                        "font" => "font-src",
                        "fetch" => "connect-src",
                        "audio" or "video" or "track" => "media-src",
                        _ => "default-src",
                    };
                    add(directive, href);
                    addSrcset("img-src", imageSrcset);
                    break;
                case "preconnect" or "dns-prefetch":
                    // Not governed by the policy, but it contacts a third party, so it must be allowlisted too.
                    add("default-src", href);
                    break;
            }
        }
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in Attribute().Matches(text))
        {
            attributes.TryAdd(attribute.Groups["name"].Value, WebUtility.HtmlDecode(attribute.Groups["value"].Value));
        }

        return attributes;
    }

    [GeneratedRegex(@"<(?<name>[a-zA-Z][a-zA-Z0-9-]*)(?<attrs>(?:\s+[^\s=>/""']+(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s""'=<>`]+))?)*)\s*/?>")]
    private static partial Regex StartTag();

    [GeneratedRegex(@"(?<name>[^\s=>/""']+)(?:\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s""'=<>`]+)))?")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"url\s*=\s*['""]?(?<url>[^'""\s;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RefreshUrl();

    [GeneratedRegex(@"url\(\s*['""]?(?<url>[^'"")]+)['""]?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrl();

    [GeneratedRegex(@"@import\s+(?:url\(\s*)?['""]?(?<url>[^'"")\s;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CssImport();

    [GeneratedRegex(@"@font-face\s*\{[^}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex FontFace();
}

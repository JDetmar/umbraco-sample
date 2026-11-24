using Microsoft.AspNetCore.Html;

namespace UmbracoSample.Web.Components;

/// <summary>A component ready to render. Views receive these, never Umbraco content (ADR 0001: no Models Builder).</summary>
public interface IComponentModel
{
    /// <summary>The partial view under Views/Partials/Components that renders this model.</summary>
    string ViewName { get; }
}

/// <summary>Opening banner. Docs: docs/components/hero.md.</summary>
public sealed record HeroModel(string Heading, string? Intro, ImageModel? Image, LinkModel? Link) : IComponentModel
{
    public string ViewName => "Hero";
}

/// <summary>Formatted editor text. Docs: docs/components/rich-text.md.</summary>
public sealed record RichTextModel(IHtmlContent Html) : IComponentModel
{
    public string ViewName => "RichText";
}

/// <summary>A grid of cards. Docs: docs/components/card-grid.md.</summary>
public sealed record CardGridModel(string? Heading, IReadOnlyList<CardModel> Cards) : IComponentModel
{
    public string ViewName => "CardGrid";

    /// <summary>
    /// Card titles sit one level below the grid heading, or at level 2 when the grid has none,
    /// so the page outline never skips a level (CMP-04, ES-035).
    /// </summary>
    public int CardHeadingLevel => Heading is null ? 2 : 3;
}

/// <summary>One card in a <see cref="CardGridModel"/>.</summary>
public sealed record CardModel(string Title, string? Text, LinkModel? Link);

/// <summary>Short prompt with one link. Docs: docs/components/call-to-action.md.</summary>
public sealed record CtaModel(string Heading, string? Text, LinkModel Link) : IComponentModel
{
    public string ViewName => "CallToAction";
}

/// <summary>A link that is safe to render.</summary>
public sealed record LinkModel(string Text, string Url, bool OpensInNewTab)
{
    private static readonly string[] AllowedSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto, "tel"];

    // Relative URLs are resolved against this host; if the result leaves it, the URL was not really site-relative.
    private static readonly Uri SentinelBase = new("https://site.invalid/");

    /// <summary>
    /// Returns null when there is no usable URL. Only http, https, mailto and tel URLs and site-relative paths are allowed,
    /// so a pasted <c>javascript:</c> or <c>data:</c> URL never reaches a page (ES-026).
    /// </summary>
    public static LinkModel? TryCreate(string? text, string? url, string? target)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        url = url.Trim();

        // Browsers drop tabs and newlines and read "\" as "/", which turns "/\evil.example" into "//evil.example".
        // No legitimate link needs them, so any such URL is rejected outright.
        if (url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\'))
        {
            return null;
        }

        if (!IsSiteRelative(url) && !IsAllowedAbsolute(url))
        {
            return null;
        }

        var label = string.IsNullOrWhiteSpace(text) ? url : text.Trim();
        return new LinkModel(label, url, string.Equals(target, "_blank", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSiteRelative(string url) =>
        (url.StartsWith('/') || url.StartsWith('#') || url.StartsWith('?'))
        && Uri.TryCreate(SentinelBase, url, out var resolved)
        && resolved.Host == SentinelBase.Host;

    private static bool IsAllowedAbsolute(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var absolute)
        && AllowedSchemes.Contains(absolute.Scheme, StringComparer.OrdinalIgnoreCase)
        && (absolute.Scheme is "mailto" or "tel" || !string.IsNullOrEmpty(absolute.Host))
        && !url.StartsWith('/');
}

/// <summary>An image with the alt text that makes it accessible.</summary>
public sealed record ImageModel(string Url, string Alt)
{
    /// <summary>
    /// Returns null when the image has no alt text: an image without a text alternative is not rendered (CMP-01, ES-035).
    /// Decorative images belong in CSS, not in content.
    /// </summary>
    public static ImageModel? TryCreate(string? url, string? alt) =>
        string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(alt) ? null : new ImageModel(url, alt.Trim());
}

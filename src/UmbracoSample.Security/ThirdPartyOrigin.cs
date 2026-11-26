namespace UmbracoSample.Security;

/// <summary>An approved third-party origin, what it is for, and the resource kinds it may serve.</summary>
/// <param name="Origin">An exact https origin, such as https://cdn.example.com.</param>
/// <param name="Purpose">Why the site loads from it; required.</param>
/// <param name="Kinds">The resource kinds it may serve; it goes only into those directives.</param>
/// <param name="Hosts">
/// The site hosts it applies to, such as www.example.com, so that one site's tag never widens another site's policy in a
/// shared instance (ADR 0020). Null applies it to every host; an empty set is rejected, so "every host" has one spelling.
/// </param>
public sealed record ThirdPartyOrigin(string Origin, string Purpose, IReadOnlySet<ResourceKind> Kinds, IReadOnlySet<string>? Hosts = null)
{
    /// <summary>True if the entry applies to pages served on <paramref name="host"/>; a null host matches only unscoped entries.</summary>
    public bool AppliesTo(string? host) =>
        Hosts is null || (host is not null && Hosts.Contains(host, StringComparer.OrdinalIgnoreCase));
}

/// <summary>The kinds of resource a page loads, one per content security policy fetch directive.</summary>
public enum ResourceKind
{
    Script,
    Style,
    Image,
    Font,
    Connect,
    Frame,
}

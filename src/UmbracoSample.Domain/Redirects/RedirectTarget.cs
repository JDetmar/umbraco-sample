using System.Diagnostics.CodeAnalysis;

namespace UmbracoSample.Domain.Redirects;

/// <summary>
/// Where a redirect sends the visitor: a path on this site (kept exactly as written, and it may carry a query),
/// or an absolute https URL. Anything else, such as javascript:, data:, http: or protocol-relative URLs, is rejected.
/// </summary>
public sealed record RedirectTarget
{
    private RedirectTarget(string location, SitePath? sitePath)
    {
        Location = location;
        SitePath = sitePath;
    }

    /// <summary>The value for the Location header.</summary>
    public string Location { get; }

    /// <summary>The normalised path when the target is on this site, otherwise null.</summary>
    public SitePath? SitePath { get; }

    public bool HasQuery => Location.Split('#', 2)[0].Contains('?', StringComparison.Ordinal);

    /// <summary>The Location for a request: its query is kept unless the target has one, and goes before any fragment.</summary>
    public string LocationFor(string? requestQuery)
    {
        if (HasQuery || string.IsNullOrEmpty(requestQuery) || requestQuery == "?")
        {
            return Location;
        }

        var query = requestQuery.StartsWith('?') ? requestQuery : "?" + requestQuery;
        var hash = Location.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? Location + query : Location[..hash] + query + Location[hash..];
    }

    public override string ToString() => Location;

    public static bool TryParse(string? text, [NotNullWhen(true)] out RedirectTarget? target, [NotNullWhen(false)] out string? error)
    {
        target = null;
        if (string.IsNullOrEmpty(text))
        {
            error = "is empty";
            return false;
        }

        if (text.Length > Redirects.SitePath.MaxLength)
        {
            error = $"is longer than {Redirects.SitePath.MaxLength} characters";
            return false;
        }

        foreach (var c in text)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c) || c is '\\' or ',' or '"' || c > '\u007f')
            {
                // Location is an HTTP header: ASCII only, so non-ASCII must be percent-encoded (or punycode for hosts).
                error = "must be ASCII without whitespace, control characters, backslashes, commas or quotes; percent-encode other characters";
                return false;
            }
        }

        if (text[0] == '/')
        {
            var pathPart = text.Split('?', 2)[0].Split('#', 2)[0];
            if (!Redirects.SitePath.TryParse(pathPart, out var path, out var pathError))
            {
                error = pathError;
                return false;
            }

            target = new RedirectTarget(text, path);
            error = null;
            return true;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "must be a site-relative path or an absolute https URL without credentials";
            return false;
        }

        target = new RedirectTarget(text, null);
        error = null;
        return true;
    }
}

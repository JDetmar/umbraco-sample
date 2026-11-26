using System.Text;
using Microsoft.AspNetCore.Http;

namespace UmbracoSample.Security;

/// <summary>
/// Response headers that harden site pages (TST-09, ES-026). Site pages, including previews, get a strict content security
/// policy with no inline script or style; the back office, which ships its own scripts, gets a minimal policy.
/// </summary>
public static class SecurityHeaders
{
    private static readonly (ResourceKind Kind, string Directive)[] FetchDirectives =
    [
        (ResourceKind.Script, "script-src"),
        (ResourceKind.Style, "style-src"),
        (ResourceKind.Image, "img-src"),
        (ResourceKind.Font, "font-src"),
        (ResourceKind.Connect, "connect-src"),
        (ResourceKind.Frame, "frame-src"),
    ];

    /// <summary>Headers sent on every response, back office included.</summary>
    public static IReadOnlyDictionary<string, string> Everywhere { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["X-Content-Type-Options"] = "nosniff",
        ["Referrer-Policy"] = "strict-origin-when-cross-origin",
        // Legacy browsers; frame-ancestors in the policy is the modern control. Same-origin, because back-office preview frames site pages.
        ["X-Frame-Options"] = "SAMEORIGIN",
    };

    /// <summary>Headers sent on site pages only.</summary>
    public static IReadOnlyDictionary<string, string> SiteOnly { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()",
        ["Cross-Origin-Opener-Policy"] = "same-origin",
    };

    /// <summary>The policy for back-office paths: no plugins, no base-URL hijack, no framing by other sites.</summary>
    public const string BackOfficePolicy = "object-src 'none'; base-uri 'self'; frame-ancestors 'self'";

    /// <summary>
    /// Builds the content security policy for site pages on <paramref name="host"/> from the entries of the third-party
    /// allowlist that apply to it. With no host, only the entries that name no hosts apply.
    /// </summary>
    public static string ContentSecurityPolicy(IEnumerable<ThirdPartyOrigin> allowlist, string? host = null)
    {
        var all = allowlist.ToList();
        Validate(all);
        var origins = all.Where(entry => entry.AppliesTo(host)).ToList();

        var policy = new StringBuilder("default-src 'self'; base-uri 'self'; object-src 'none'; form-action 'self'; frame-ancestors 'self'");
        foreach (var (kind, directive) in FetchDirectives)
        {
            policy.Append("; ").Append(directive).Append(" 'self'");
            if (kind == ResourceKind.Image)
            {
                // Inline SVG and small images in the stylesheet; data: cannot run script in an img context.
                policy.Append(" data:");
            }

            foreach (var origin in origins.Where(o => o.Kinds.Contains(kind)).Select(o => o.Origin).Order(StringComparer.Ordinal))
            {
                policy.Append(' ').Append(origin);
            }
        }

        return policy.ToString();
    }

    /// <summary>
    /// Rejects allowlist entries that would weaken the policy: wildcards, plain http, paths, no stated purpose, or a
    /// host that is not a plain lower-case DNS name.
    /// </summary>
    public static void Validate(IEnumerable<ThirdPartyOrigin> allowlist)
    {
        foreach (var entry in allowlist)
        {
            if (string.IsNullOrWhiteSpace(entry.Purpose))
            {
                throw new ArgumentException($"Allowlist entry {entry.Origin} has no purpose.", nameof(allowlist));
            }

            if (entry.Kinds.Count == 0)
            {
                throw new ArgumentException($"Allowlist entry {entry.Origin} allows no resource kinds.", nameof(allowlist));
            }

            if (entry.Origin.Contains('*', StringComparison.Ordinal)
                || !Uri.TryCreate(entry.Origin, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(entry.Origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
            {
                throw new ArgumentException($"Allowlist entry {entry.Origin} must be an exact https origin, such as https://cdn.example.com, with no wildcard or path.", nameof(allowlist));
            }

            foreach (var host in entry.Hosts ?? Enumerable.Empty<string>())
            {
                if (!IsPlainHost(host))
                {
                    throw new ArgumentException($"Allowlist entry {entry.Origin} names host '{host}'; a host is a lower-case DNS name such as www.example.com, with no scheme, port, path or wildcard.", nameof(allowlist));
                }
            }
        }
    }

    private static bool IsPlainHost(string host) =>
        !string.IsNullOrEmpty(host)
        && string.Equals(host, host.ToLowerInvariant(), StringComparison.Ordinal)
        && Uri.CheckHostName(host) == UriHostNameType.Dns;

    /// <summary>True for back-office paths, which keep Umbraco's own scripts and are not constrained by the site policy.</summary>
    public static bool IsBackOffice(PathString path) => path.StartsWithSegments("/umbraco", StringComparison.OrdinalIgnoreCase);

    /// <summary>Applies the headers to one response.</summary>
    /// <param name="request">The request being answered.</param>
    /// <param name="headers">The response headers to set.</param>
    /// <param name="contentSecurityPolicy">The site policy, from <see cref="ContentSecurityPolicy"/>.</param>
    public static void Apply(HttpRequest request, IHeaderDictionary headers, string contentSecurityPolicy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var (name, value) in Everywhere)
        {
            headers[name] = value;
        }

        if (IsBackOffice(request.Path))
        {
            // Umbraco owns the back office's scripts and styles; this minimal policy constrains only what never breaks it.
            headers.ContentSecurityPolicy = BackOfficePolicy;
            return;
        }

        foreach (var (name, value) in SiteOnly)
        {
            headers[name] = value;
        }

        // Preview is not exempt: Umbraco 17's preview badge is a same-origin script that styles itself with
        // adopted stylesheets, which style-src does not govern, so it works under the site policy.
        headers.ContentSecurityPolicy = contentSecurityPolicy;
    }
}

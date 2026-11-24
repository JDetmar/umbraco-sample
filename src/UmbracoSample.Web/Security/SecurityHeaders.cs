using System.Text;
using Microsoft.AspNetCore.Http;

namespace UmbracoSample.Web.Security;

/// <summary>
/// Response headers that harden site pages (TST-09, ES-026). Site pages get a strict content security policy with no
/// inline script or style; the back office, which ships its own scripts, gets the headers that do not constrain it.
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

    /// <summary>Builds the content security policy for site pages from the third-party allowlist.</summary>
    public static string ContentSecurityPolicy(IEnumerable<ThirdPartyOrigin> allowlist)
    {
        var origins = allowlist.ToList();
        Validate(origins);

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

    /// <summary>Rejects allowlist entries that would weaken the policy: wildcards, plain http, paths, or no stated purpose.</summary>
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
        }
    }

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
            return;
        }

        foreach (var (name, value) in SiteOnly)
        {
            headers[name] = value;
        }

        // Back-office preview renders site pages for signed-in editors and injects Umbraco's preview script.
        // Umbraco honours the preview cookie only with a back-office session, so dropping the policy here
        // only affects editors (and anyone who sets the cookie in their own browser).
        if (!request.Cookies.ContainsKey(Umbraco.Cms.Core.Constants.Web.PreviewCookieName))
        {
            headers.ContentSecurityPolicy = contentSecurityPolicy;
        }
    }
}

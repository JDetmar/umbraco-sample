using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace UmbracoSample.Security;

public static class SecurityHeadersMiddleware
{
    /// <summary>
    /// Adds the security headers to every response, including static files and error pages, with the content security
    /// policy built from the host's <paramref name="allowlist"/>. Register it before Umbraco so that it wraps the whole
    /// pipeline. Every policy is built, and the allowlist validated, once at startup.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IReadOnlyList<ThirdPartyOrigin> allowlist)
    {
        var policies = SitePolicies.From(allowlist);

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                SecurityHeaders.Apply(context.Request, context.Response.Headers, policies.For(context.Request.Host.Host));
                return Task.CompletedTask;
            });
            return next(context);
        });
    }
}

/// <summary>
/// The site content security policy for each host (ADR 0020): one for every host an allowlist entry names, and one for
/// every other host. Built up front, so a request's Host header only selects a policy and never grows a cache.
/// </summary>
public sealed class SitePolicies
{
    private readonly Dictionary<string, string> _byHost;
    private readonly string _other;

    private SitePolicies(Dictionary<string, string> byHost, string other)
    {
        _byHost = byHost;
        _other = other;
    }

    public static SitePolicies From(IReadOnlyList<ThirdPartyOrigin> allowlist)
    {
        ArgumentNullException.ThrowIfNull(allowlist);
        SecurityHeaders.Validate(allowlist);

        var hosts = allowlist.SelectMany(entry => entry.Hosts ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase);
        var byHost = hosts.ToDictionary(host => host, host => SecurityHeaders.ContentSecurityPolicy(allowlist, host), StringComparer.OrdinalIgnoreCase);
        return new SitePolicies(byHost, SecurityHeaders.ContentSecurityPolicy(allowlist));
    }

    /// <summary>The policy for pages served on <paramref name="host"/>.</summary>
    public string For(string? host) => host is not null && _byHost.TryGetValue(host, out var policy) ? policy : _other;
}

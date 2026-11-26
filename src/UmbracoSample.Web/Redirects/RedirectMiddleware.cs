using UmbracoSample.Domain.Redirects;

namespace UmbracoSample.Web.Redirects;

/// <summary>
/// Serves the redirect map (TST-08). The maps are parsed once at startup; an invalid map stops the site from starting,
/// so a bad deploy fails its readiness check instead of serving broken redirects.
/// </summary>
public static class RedirectMiddleware
{
    /// <summary>The map for every host without its own.</summary>
    public const string FileName = "redirects.csv";

    /// <summary>Paths a redirect may never capture.</summary>
    public static IReadOnlyList<string> ReservedPrefixes { get; } = ["/umbraco", "/health", "/dist", "/media", "/App_Plugins"];

    /// <summary>
    /// Loads <see cref="FileName"/> and any host map named <c>redirects.&lt;host&gt;.csv</c>, such as
    /// <c>redirects.www.example.com.csv</c> (ADR 0020). Throws with every error of every file if any is invalid.
    /// </summary>
    public static SiteRedirects Load(string contentRootPath)
    {
        var path = Path.Join(contentRootPath, FileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The redirect map {path} is missing.");
        }

        var errors = new List<string>();
        var fallback = Parse(path, errors);
        var byHost = new Dictionary<string, RedirectMap>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(contentRootPath, "redirects.*.csv").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            var host = name["redirects.".Length..^".csv".Length];
            if (!SiteRedirects.IsPlainHost(host))
            {
                errors.Add($"{name}: '{host}' is not a host; name a host map redirects.<host>.csv with a lower-case DNS name, such as redirects.www.example.com.csv.");
                continue;
            }

            if (Parse(file, errors) is { } map)
            {
                byHost[host] = map;
            }
        }

        return errors.Count == 0 && fallback is not null
            ? new SiteRedirects(fallback, byHost)
            : throw new InvalidOperationException($"The redirect map is invalid:\n{string.Join('\n', errors)}");
    }

    /// <summary>Register before Umbraco so a redirected path never reaches content routing.</summary>
    public static IApplicationBuilder UseRedirectMap(this IApplicationBuilder app, SiteRedirects redirects)
    {
        ArgumentNullException.ThrowIfNull(redirects);

        return app.Use((context, next) =>
        {
            var request = context.Request;
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                return next(context);
            }

            var rule = redirects.For(request.Host.Host).Find(request.Path.Value);
            if (rule is null)
            {
                return next(context);
            }

            // Keep campaign and tracking parameters unless the target sets its own query.
            var location = rule.Target.LocationFor(request.QueryString.Value);

            context.Response.StatusCode = (int)rule.Kind;
            context.Response.Headers.Location = location;
            return Task.CompletedTask;
        });
    }

    private static RedirectMap? Parse(string file, List<string> errors)
    {
        var (map, fileErrors) = RedirectMap.Parse(File.ReadAllText(file), ReservedPrefixes);
        errors.AddRange(fileErrors.Select(error => $"{Path.GetFileName(file)} {error}"));
        return map;
    }
}

/// <summary>
/// The redirect map for each host (ADR 0020). A host with its own map uses only that map, so one site's rules never
/// apply to another site in a shared instance; every other host uses the default map.
/// </summary>
public sealed class SiteRedirects(RedirectMap fallback, IReadOnlyDictionary<string, RedirectMap> byHost)
{
    /// <summary>The map for hosts without their own.</summary>
    public RedirectMap Default { get; } = fallback;

    /// <summary>The hosts with their own map, and those maps.</summary>
    public IReadOnlyDictionary<string, RedirectMap> ByHost { get; } = byHost;

    /// <summary>One map for every host.</summary>
    public static SiteRedirects ForEveryHost(RedirectMap map) => new(map, new Dictionary<string, RedirectMap>());

    /// <summary>The map that serves requests for <paramref name="host"/>.</summary>
    public RedirectMap For(string? host) => host is not null && ByHost.TryGetValue(host, out var map) ? map : Default;

    internal static bool IsPlainHost(string host) =>
        host.Length > 0
        && string.Equals(host, host.ToLowerInvariant(), StringComparison.Ordinal)
        && Uri.CheckHostName(host) == UriHostNameType.Dns;
}

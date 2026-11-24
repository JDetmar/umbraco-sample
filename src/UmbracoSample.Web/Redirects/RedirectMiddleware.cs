using UmbracoSample.Domain.Redirects;

namespace UmbracoSample.Web.Redirects;

/// <summary>
/// Serves the redirect map (TST-08). The map is parsed once at startup; an invalid map stops the site from starting,
/// so a bad deploy fails its readiness check instead of serving broken redirects.
/// </summary>
public static class RedirectMiddleware
{
    public const string FileName = "redirects.csv";

    /// <summary>Paths a redirect may never capture.</summary>
    public static IReadOnlyList<string> ReservedPrefixes { get; } = ["/umbraco", "/health", "/dist", "/media", "/App_Plugins"];

    public static RedirectMap Load(string contentRootPath)
    {
        var path = Path.Join(contentRootPath, FileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The redirect map {path} is missing.");
        }

        var (map, errors) = RedirectMap.Parse(File.ReadAllText(path), ReservedPrefixes);
        return map ?? throw new InvalidOperationException($"The redirect map {FileName} is invalid:\n{string.Join('\n', errors)}");
    }

    /// <summary>Register before Umbraco so a redirected path never reaches content routing.</summary>
    public static IApplicationBuilder UseRedirectMap(this IApplicationBuilder app, RedirectMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return app.Use((context, next) =>
        {
            var request = context.Request;
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                return next(context);
            }

            var rule = map.Find(request.Path.Value);
            if (rule is null)
            {
                return next(context);
            }

            // Keep campaign and tracking parameters unless the target sets its own query.
            var location = rule.Target.HasQuery || !request.QueryString.HasValue
                ? rule.Target.Location
                : rule.Target.Location + request.QueryString.Value;

            context.Response.StatusCode = (int)rule.Kind;
            context.Response.Headers.Location = location;
            return Task.CompletedTask;
        });
    }
}

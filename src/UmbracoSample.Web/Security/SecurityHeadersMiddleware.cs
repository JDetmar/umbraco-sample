namespace UmbracoSample.Web.Security;

public static class SecurityHeadersMiddleware
{
    /// <summary>
    /// Adds the security headers to every response, including static files and error pages.
    /// Register it before Umbraco so that it wraps the whole pipeline.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        var policy = SecurityHeaders.ContentSecurityPolicy(ThirdPartyAllowlist.Origins);

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                SecurityHeaders.Apply(context.Request, context.Response.Headers, policy);
                return Task.CompletedTask;
            });
            return next(context);
        });
    }
}

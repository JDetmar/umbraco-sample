using Microsoft.AspNetCore.Http.Features;
using UmbracoSample.Leads;

namespace UmbracoSample.Web.Leads;

/// <summary>
/// Caps every front-end request body at 32 KB (threat model 0001, D3). The lead form is the only thing on the public
/// site that accepts a body; the back office and its API, under <c>/umbraco</c>, keep the server's own limits for media
/// uploads. Umbraco also routes every surface controller under <c>/umbraco/surface</c>, so that path is capped too. It
/// runs before routing, antiforgery and the rate limiter, so an oversized post is never read.
/// </summary>
public static class FrontEndBodyLimit
{
    public static IApplicationBuilder UseFrontEndBodyLimit(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/umbraco", StringComparison.OrdinalIgnoreCase)
            && !context.Request.Path.StartsWithSegments("/umbraco/surface", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (context.Request.ContentLength > LeadFormOptions.MaxBodyBytes)
        {
            await TooLarge(context);
            return;
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = LeadFormOptions.MaxBodyBytes;
        }

        try
        {
            await next(context);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            // A chunked body with no Content-Length that grows past the cap.
            await TooLarge(context);
        }
    });

    private static Task TooLarge(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync("This form submission is too large. Please shorten your message and try again.");
    }
}

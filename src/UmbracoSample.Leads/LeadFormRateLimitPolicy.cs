using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace UmbracoSample.Leads;

/// <summary>
/// The lead form's own rate limit (threat model 0001, D3): by default 3 submissions per 10 minutes per client, counted in
/// memory on each replica, so N replicas allow up to N×3. Every attempt counts, including rejected ones. The edge WAF is
/// the global limit. A refused post gets 429 and is never cached.
/// </summary>
public sealed class LeadFormRateLimitPolicy(IOptions<LeadFormOptions> form, IOptions<EdgeOptions> edge) : IRateLimiterPolicy<string>
{
    public const string Name = "lead-form";

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return ValueTask.CompletedTask;
    };

    public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIdentity.Resolve(httpContext, edge.Value), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = form.Value.PermitLimit,
            Window = form.Value.Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
}

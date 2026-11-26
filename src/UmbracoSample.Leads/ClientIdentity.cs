using System.Net;
using Microsoft.AspNetCore.Http;

namespace UmbracoSample.Leads;

/// <summary>Where the edge sits (threat model 0001, boundary 2). Bound from <c>Leads:Edge</c>.</summary>
public sealed class EdgeOptions
{
    public const string Section = "Leads:Edge";

    /// <summary>This deployment's Front Door ID. Unset until the edge exists; then every request uses the connection IP.</summary>
    public string? FrontDoorId { get; set; }
}

/// <summary>
/// The client a rate limit counts (threat model 0001, automated abuse). The edge's client IP header is believed only on a
/// request that carries this deployment's Front Door ID header; otherwise the connection IP is used, so a forged
/// <c>X-Forwarded-For</c> or client IP header changes nothing. The value is used for counting and is never logged.
/// </summary>
public static class ClientIdentity
{
    public const string FrontDoorIdHeader = "X-Azure-FDID";
    public const string ClientIpHeader = "X-Azure-ClientIP";

    public static string Resolve(HttpContext context, EdgeOptions edge)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(edge);
        var headers = context.Request.Headers;
        if (!string.IsNullOrEmpty(edge.FrontDoorId)
            && headers[FrontDoorIdHeader] is [var id] && string.Equals(id, edge.FrontDoorId, StringComparison.Ordinal)
            && headers[ClientIpHeader] is [var forwarded] && IPAddress.TryParse(forwarded, out var client))
        {
            return client.ToString();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

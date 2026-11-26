using System.Net;
using Microsoft.AspNetCore.Http;

namespace UmbracoSample.Leads;

/// <summary>Where the edge sits (threat model 0001, boundary 2). Bound from <c>Leads:Edge</c>.</summary>
public sealed class EdgeOptions
{
    public const string Section = "Leads:Edge";

    /// <summary>
    /// This deployment's Front Door ID. Unset until the edge exists; then every request uses the connection IP. Trusting
    /// the edge's client IP header is safe only once the origin accepts traffic from Front Door alone (threat model 0001,
    /// R2 and DEV-010); until then the ID must stay unset.
    /// </summary>
    public string? FrontDoorId { get; set; }
}

/// <summary>
/// The client a rate limit counts (threat model 0001, automated abuse). The edge's client IP header is believed only on a
/// request that carries this deployment's Front Door ID header; otherwise the connection IP is used, so a forged
/// <c>X-Forwarded-For</c> or client IP header changes nothing. An IPv6 client counts as its /64 network, because one
/// subscriber normally holds a whole /64 and could otherwise use a new address for every post. The value is used for
/// counting and is never logged.
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
            return Key(client);
        }

        return context.Connection.RemoteIpAddress is { } connection ? Key(connection) : "unknown";
    }

    private static string Key(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        Span<byte> bytes = stackalloc byte[16];
        _ = address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return $"{new IPAddress(bytes)}/64";
    }
}

using System.Net;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace UmbracoSample.Leads.Tests;

/// <summary>Threat model 0001: the client IP header is believed only with this deployment's Front Door ID.</summary>
public sealed class ClientIdentityTests
{
    private static readonly EdgeOptions Edge = new() { FrontDoorId = "fd-1234" };

    private static DefaultHttpContext Request(params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }

        return context;
    }

    [Fact]
    public void With_this_deployments_front_door_id_the_edge_client_ip_is_used() =>
        ClientIdentity.Resolve(Request(("X-Azure-FDID", "fd-1234"), ("X-Azure-ClientIP", "203.0.113.7")), Edge).ShouldBe("203.0.113.7");

    [Theory]
    [InlineData("X-Forwarded-For", "203.0.113.7")]
    [InlineData("X-Azure-ClientIP", "203.0.113.7")]
    public void A_forged_client_header_without_the_front_door_id_is_ignored(string header, string value) =>
        ClientIdentity.Resolve(Request((header, value)), Edge).ShouldBe("10.0.0.5");

    [Fact]
    public void Another_front_door_id_is_ignored() =>
        ClientIdentity.Resolve(Request(("X-Azure-FDID", "fd-other"), ("X-Azure-ClientIP", "203.0.113.7")), Edge).ShouldBe("10.0.0.5");

    [Fact]
    public void Without_a_configured_front_door_every_request_uses_the_connection() =>
        ClientIdentity.Resolve(Request(("X-Azure-FDID", ""), ("X-Azure-ClientIP", "203.0.113.7")), new EdgeOptions()).ShouldBe("10.0.0.5");

    [Theory]
    [InlineData("2001:db8:1:2:aaaa::1", "2001:db8:1:2::1:5")]
    [InlineData("2001:db8:1:2::", "2001:db8:1:2:ffff:ffff:ffff:ffff")]
    public void Ipv6_addresses_in_one_64_network_are_one_client(string first, string second)
    {
        string Of(string ip) => ClientIdentity.Resolve(Request(("X-Azure-FDID", "fd-1234"), ("X-Azure-ClientIP", ip)), Edge);

        Of(first).ShouldBe(Of(second));
        Of(first).ShouldBe("2001:db8:1:2::/64");
        Of("2001:db8:1:3::1").ShouldNotBe(Of(first));
    }

    [Fact]
    public void An_ipv4_mapped_connection_counts_as_its_ipv4_address()
    {
        var context = Request();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:10.0.0.5");

        ClientIdentity.Resolve(context, Edge).ShouldBe("10.0.0.5");
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("203.0.113.7, 198.51.100.1")]
    public void An_unparseable_edge_client_ip_falls_back_to_the_connection(string value) =>
        ClientIdentity.Resolve(Request(("X-Azure-FDID", "fd-1234"), ("X-Azure-ClientIP", value)), Edge).ShouldBe("10.0.0.5");
}

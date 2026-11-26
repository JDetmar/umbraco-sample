using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace UmbracoSample.Leads.Tests;

/// <summary>
/// Threat model 0001, automated abuse: the form's own rate-limit policy on a real Kestrel server, 3 posts per window per
/// client. The connection is always loopback here, so distinct clients are made with the Front Door headers.
/// </summary>
public sealed class LeadFormRateLimitTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _http;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Leads:Map:BaseAddress"] = "http://127.0.0.1:9/",
            ["Leads:Map:ApiKey"] = "test-key-not-a-secret",
            ["Leads:Edge:FrontDoorId"] = "fd-1234",
        });
        builder.Services.AddLeadForm(builder.Configuration);
        builder.Services.AddSingleton<ILeadFormCatalog, NoForms>();
        _app = builder.Build();
        _app.UseRateLimiter();
        _app.MapPost("/form", () => Results.Ok()).RequireRateLimiting(LeadFormRateLimitPolicy.Name);
        await _app.StartAsync(TestContext.Current.CancellationToken);
        var address = _app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        _http = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_fourth_post_from_one_client_gets_429_and_is_not_cached()
    {
        for (var i = 0; i < 3; i++)
        {
            (await Post()).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var refused = await Post();
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task A_forged_client_ip_without_the_front_door_id_still_counts_as_the_same_client()
    {
        for (var i = 0; i < 3; i++)
        {
            (await Post(("X-Azure-ClientIP", $"203.0.113.{i}"), ("X-Forwarded-For", $"198.51.100.{i}"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await Post(("X-Azure-ClientIP", "203.0.113.99"))).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Two_clients_behind_the_edge_are_counted_separately()
    {
        for (var i = 0; i < 3; i++)
        {
            (await Post(("X-Azure-FDID", "fd-1234"), ("X-Azure-ClientIP", "203.0.113.1"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await Post(("X-Azure-FDID", "fd-1234"), ("X-Azure-ClientIP", "203.0.113.1"))).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Post(("X-Azure-FDID", "fd-1234"), ("X-Azure-ClientIP", "203.0.113.2"))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> Post(params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/form") { Content = new StringContent(string.Empty) };
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return await _http!.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class NoForms : ILeadFormCatalog
    {
        public LeadFormSettings? Find(Guid blockKey) => null;
    }
}

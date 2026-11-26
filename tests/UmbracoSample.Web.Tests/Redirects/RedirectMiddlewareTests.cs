using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using UmbracoSample.Domain.Redirects;
using UmbracoSample.Web.Redirects;

namespace UmbracoSample.Web.Tests.Redirects;

public sealed class RedirectMiddlewareTests
{
    private const int PassedThrough = 299;

    private static readonly RedirectMap Map = RedirectMap.Parse(
        "source,target,kind\n/old,/new,permanent\n/promo,/new?utm_source=promo,temporary\n/away,https://example.com/x,permanent\n/frag,/new#top,permanent\n",
        RedirectMiddleware.ReservedPrefixes).Map!;

    [Fact]
    public void The_committed_redirect_map_is_valid()
    {
        // The map is copied next to the site; this fails fast, before the site would refuse to start.
        var redirects = RedirectMiddleware.Load(AppContext.BaseDirectory);

        redirects.Default.Rules.ShouldNotBeEmpty();
    }

    [Fact]
    public void No_host_map_is_committed_until_the_integration_tests_follow_it()
    {
        // The integration test follows every rule of redirects.csv on the real stack (TST-08), but sends no Host header,
        // so it cannot reach a host map. The first host map must extend that test, then remove this guard (ADR 0020).
        RedirectMiddleware.Load(AppContext.BaseDirectory).ByHost.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("GET", "/old", "", 301, "/new")]
    [InlineData("HEAD", "/OLD/", "", 301, "/new")]
    [InlineData("GET", "/old", "?utm_campaign=x", 301, "/new?utm_campaign=x")]
    [InlineData("GET", "/promo", "?utm_campaign=x", 302, "/new?utm_source=promo")]
    [InlineData("GET", "/away", "", 301, "https://example.com/x")]
    [InlineData("GET", "/frag", "?x=1", 301, "/new?x=1#top")]
    public void Matching_requests_are_redirected(string method, string path, string query, int status, string location)
    {
        var context = Invoke(method, path, query);

        context.Response.StatusCode.ShouldBe(status);
        context.Response.Headers.Location.ToString().ShouldBe(location);
    }

    [Theory]
    [InlineData("POST", "/old")]
    [InlineData("GET", "/new")]
    [InlineData("GET", "/old/child")]
    public void Other_requests_pass_through(string method, string path)
    {
        Invoke(method, path, string.Empty).Response.StatusCode.ShouldBe(PassedThrough);
    }

    [Fact]
    public void A_missing_or_invalid_map_stops_startup_with_every_error()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            Should.Throw<InvalidOperationException>(() => RedirectMiddleware.Load(folder.FullName)).Message.ShouldContain("missing");

            File.WriteAllText(Path.Join(folder.FullName, RedirectMiddleware.FileName), "source,target,kind\n/umbraco/x,/a,permanent\n/a,/a,gone\n");
            var message = Should.Throw<InvalidOperationException>(() => RedirectMiddleware.Load(folder.FullName)).Message;
            message.ShouldContain("line 2");
            message.ShouldContain("line 3");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_host_map_replaces_the_default_map_for_that_host_only()
    {
        // ADR 0020: one site's rules never apply to another site in a shared instance.
        WithMaps(
            new()
            {
                [RedirectMiddleware.FileName] = "source,target,kind\n/old,/new,permanent\n",
                ["redirects.one.example.com.csv"] = "source,target,kind\n/offer,/offers,temporary\n",
            },
            folder =>
            {
                var redirects = RedirectMiddleware.Load(folder);

                redirects.ByHost.Keys.ShouldBe(["one.example.com"]);
                Invoke("GET", "/offer", string.Empty, redirects, "ONE.example.com").Response.StatusCode.ShouldBe(302);
                Invoke("GET", "/old", string.Empty, redirects, "one.example.com").Response.StatusCode.ShouldBe(PassedThrough, "a host map replaces the default; it does not add to it");
                Invoke("GET", "/old", string.Empty, redirects, "two.example.com").Response.StatusCode.ShouldBe(301);
                Invoke("GET", "/offer", string.Empty, redirects, "two.example.com").Response.StatusCode.ShouldBe(PassedThrough);
            });
    }

    [Fact]
    public void Every_error_in_every_map_stops_startup_and_names_its_file()
    {
        WithMaps(
            new()
            {
                [RedirectMiddleware.FileName] = "source,target,kind\n/a,/a,gone\n",
                ["redirects.one.example.com.csv"] = "source,target,kind\n/umbraco/x,/b,permanent\n",
                ["redirects.Not_A_Host.csv"] = "source,target,kind\n",
            },
            folder =>
            {
                var message = Should.Throw<InvalidOperationException>(() => RedirectMiddleware.Load(folder)).Message;

                message.ShouldContain("redirects.csv line 2");
                message.ShouldContain("redirects.one.example.com.csv line 2");
                message.ShouldContain("'Not_A_Host' is not a host");
            });
    }

    private static void WithMaps(Dictionary<string, string> files, Action<string> test)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            foreach (var (name, text) in files)
            {
                File.WriteAllText(Path.Join(folder.FullName, name), text);
            }

            test(folder.FullName);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static DefaultHttpContext Invoke(string method, string path, string query) =>
        Invoke(method, path, query, SiteRedirects.ForEveryHost(Map), "localhost");

    private static DefaultHttpContext Invoke(string method, string path, string query, SiteRedirects redirects, string host)
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseRedirectMap(redirects);
        app.Run(context =>
        {
            context.Response.StatusCode = PassedThrough;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query.Length == 0 ? null : query);
        app.Build()(context).GetAwaiter().GetResult();
        return context;
    }
}

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
        "source,target,kind\n/old,/new,permanent\n/promo,/new?utm_source=promo,temporary\n/away,https://example.com/x,permanent\n",
        RedirectMiddleware.ReservedPrefixes).Map!;

    [Fact]
    public void The_committed_redirect_map_is_valid()
    {
        // The map is copied next to the site; this fails fast, before the site would refuse to start.
        var map = RedirectMiddleware.Load(AppContext.BaseDirectory);

        map.Rules.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("GET", "/old", "", 301, "/new")]
    [InlineData("HEAD", "/OLD/", "", 301, "/new")]
    [InlineData("GET", "/old", "?utm_campaign=x", 301, "/new?utm_campaign=x")]
    [InlineData("GET", "/promo", "?utm_campaign=x", 302, "/new?utm_source=promo")]
    [InlineData("GET", "/away", "", 301, "https://example.com/x")]
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

    private static DefaultHttpContext Invoke(string method, string path, string query)
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseRedirectMap(Map);
        app.Run(context =>
        {
            context.Response.StatusCode = PassedThrough;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query.Length == 0 ? null : query);
        app.Build()(context).GetAwaiter().GetResult();
        return context;
    }
}

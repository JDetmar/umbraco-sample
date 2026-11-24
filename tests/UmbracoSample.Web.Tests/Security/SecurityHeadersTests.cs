using Microsoft.AspNetCore.Http;
using Shouldly;
using UmbracoSample.Web.Security;

namespace UmbracoSample.Web.Tests.Security;

public sealed class SecurityHeadersTests
{
    private static readonly HashSet<ResourceKind> ScriptOnly = [ResourceKind.Script];

    [Fact]
    public void Policy_with_an_empty_allowlist_allows_only_this_site()
    {
        SecurityHeaders.ContentSecurityPolicy([]).ShouldBe(
            "default-src 'self'; base-uri 'self'; object-src 'none'; form-action 'self'; frame-ancestors 'self'; "
            + "script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-src 'self'");
    }

    [Fact]
    public void An_allowlisted_origin_appears_only_in_the_directives_it_is_approved_for()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy([new ThirdPartyOrigin("https://cdn.example.com", "Test", ScriptOnly)]);

        Directive(policy, "script-src").ShouldBe("script-src 'self' https://cdn.example.com");
        Directive(policy, "img-src").ShouldNotContain("cdn.example.com");
        Directive(policy, "connect-src").ShouldNotContain("cdn.example.com");
    }

    [Fact]
    public void The_policy_never_allows_inline_or_eval_or_wildcards()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy(ThirdPartyAllowlist.Origins);

        policy.ShouldNotContain("unsafe-");
        policy.ShouldNotContain("*");
        policy.ShouldNotContain("http:");
    }

    [Theory]
    [InlineData("http://cdn.example.com", "Test")]
    [InlineData("https://*.example.com", "Test")]
    [InlineData("https://cdn.example.com/path", "Test")]
    [InlineData("https://cdn.example.com/", "Test")]
    [InlineData("cdn.example.com", "Test")]
    [InlineData("https://cdn.example.com", " ")]
    public void Unsafe_or_undocumented_allowlist_entries_are_rejected(string origin, string purpose)
    {
        Should.Throw<ArgumentException>(() => SecurityHeaders.Validate([new ThirdPartyOrigin(origin, purpose, ScriptOnly)]));
    }

    [Fact]
    public void An_entry_that_allows_nothing_is_rejected()
    {
        Should.Throw<ArgumentException>(() => SecurityHeaders.Validate([new ThirdPartyOrigin("https://cdn.example.com", "Test", new HashSet<ResourceKind>())]));
    }

    [Fact]
    public void The_committed_allowlist_is_valid()
    {
        Should.NotThrow(() => SecurityHeaders.Validate(ThirdPartyAllowlist.Origins));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/fixture-maximal/")]
    [InlineData("/dist/site.css")]
    public void Site_responses_get_every_header_and_the_policy(string path)
    {
        var context = Request(path);

        SecurityHeaders.Apply(context.Request, context.Response.Headers, "policy");

        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy.ToString().ShouldBe("policy");
        headers.XContentTypeOptions.ToString().ShouldBe("nosniff");
        headers.XFrameOptions.ToString().ShouldBe("SAMEORIGIN");
        headers["Referrer-Policy"].ToString().ShouldBe("strict-origin-when-cross-origin");
        headers["Permissions-Policy"].ToString().ShouldContain("camera=()");
        headers["Cross-Origin-Opener-Policy"].ToString().ShouldBe("same-origin");
    }

    [Theory]
    [InlineData("/umbraco")]
    [InlineData("/umbraco/backoffice")]
    [InlineData("/UMBRACO/management/api/v1/server/status")]
    public void Back_office_responses_get_the_general_headers_and_only_the_minimal_policy(string path)
    {
        var context = Request(path);

        SecurityHeaders.Apply(context.Request, context.Response.Headers, "policy");

        var headers = context.Response.Headers;
        headers.XContentTypeOptions.ToString().ShouldBe("nosniff");
        headers.XFrameOptions.ToString().ShouldBe("SAMEORIGIN");
        headers.ContentSecurityPolicy.ToString().ShouldBe(SecurityHeaders.BackOfficePolicy);
        headers.ContainsKey("Cross-Origin-Opener-Policy").ShouldBeFalse();
    }

    [Fact]
    public void A_path_that_only_starts_with_the_back_office_name_is_a_site_page()
    {
        SecurityHeaders.IsBackOffice("/umbracofake").ShouldBeFalse();
    }

    [Fact]
    public void Preview_requests_keep_the_site_policy()
    {
        var context = Request("/fixture-maximal/");
        context.Request.Headers.Cookie = $"{Umbraco.Cms.Core.Constants.Web.PreviewCookieName}=preview";

        SecurityHeaders.Apply(context.Request, context.Response.Headers, "policy");

        context.Response.Headers.ContentSecurityPolicy.ToString().ShouldBe("policy");
    }

    private static DefaultHttpContext Request(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    private static string Directive(string policy, string name) =>
        policy.Split("; ").Single(part => part.StartsWith(name + " ", StringComparison.Ordinal));
}

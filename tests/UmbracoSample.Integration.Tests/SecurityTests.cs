using System.Text.RegularExpressions;
using Shouldly;
using UmbracoSample.Tests.Support;

namespace UmbracoSample.Integration.Tests;

/// <summary>
/// Security headers (TST-09) and the third-party allowlist (TST-10, CMP-02) on the real stack. Every resource a page
/// references must be allowed by the page's own content security policy, which the site builds from the allowlist,
/// so a new tracker fails here before a browser would block it.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class SecurityTests(SiteFixture site)
{
    public static TheoryData<string> Pages => ["/", "/fixture-empty/", "/fixture-minimal/", "/fixture-maximal/"];

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Pages_send_the_security_headers_and_a_strict_policy(string path)
    {
        using var response = await site.Web.GetAsync(path, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var policy = Header(response, "Content-Security-Policy");
        policy.ShouldStartWith("default-src 'self'");
        policy.ShouldContain("frame-ancestors 'self'");
        policy.ShouldContain("object-src 'none'");
        policy.ShouldNotContain("unsafe-");
        policy.ShouldNotContain("*");

        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "X-Frame-Options").ShouldBe("SAMEORIGIN");
        Header(response, "Referrer-Policy").ShouldBe("strict-origin-when-cross-origin");
        Header(response, "Cross-Origin-Opener-Policy").ShouldBe("same-origin");
        Header(response, "Permissions-Policy").ShouldContain("camera=()");
        response.Headers.Contains("Server").ShouldBeFalse("the server software is not advertised");
    }

    [Fact]
    public async Task Not_found_responses_get_the_site_policy()
    {
        using var response = await site.Web.GetAsync("/this-page-does-not-exist/", TestContext.Current.CancellationToken);

        ((int)response.StatusCode).ShouldBe(404);
        Header(response, "Content-Security-Policy").ShouldStartWith("default-src 'self'");
        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
    }

    [Fact]
    public async Task A_preview_cookie_does_not_remove_the_policy()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", "UMB_PREVIEW=preview");

        using var response = await site.Web.SendAsync(request, TestContext.Current.CancellationToken);

        Header(response, "Content-Security-Policy").ShouldStartWith("default-src 'self'");
    }

    [Fact]
    public async Task The_back_office_gets_the_minimal_policy()
    {
        using var response = await site.Web.GetAsync("/umbraco", TestContext.Current.CancellationToken);

        Header(response, "Content-Security-Policy").ShouldBe("object-src 'none'; base-uri 'self'; frame-ancestors 'self'");
        Header(response, "X-Frame-Options").ShouldBe("SAMEORIGIN");
    }

    [Fact]
    public async Task Static_files_and_health_endpoints_get_the_headers_too()
    {
        foreach (var path in new[] { "/dist/site.css", "/health/live" })
        {
            using var response = await site.Web.GetAsync(path, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            Header(response, "X-Content-Type-Options").ShouldBe("nosniff", path);
        }
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Pages_load_nothing_the_policy_does_not_allow(string path)
    {
        using var response = await site.Web.GetAsync(path, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var policy = Header(response, "Content-Security-Policy");
        var page = new Uri(site.Web.BaseAddress!, path);

        var references = ResourceReferences.InHtml(html).ToList();

        // Same-origin stylesheets are fetched too: an @import or url() inside them loads third-party content.
        foreach (var stylesheet in references.Where(reference => reference.Directive == "style-src").Select(reference => new Uri(page, reference.Url)).ToList())
        {
            if (stylesheet.GetLeftPart(UriPartial.Authority) == page.GetLeftPart(UriPartial.Authority))
            {
                var css = await site.Web.GetStringAsync(stylesheet, TestContext.Current.CancellationToken);
                references.AddRange(ResourceReferences.InCss(css).Select(reference => reference with { Url = new Uri(stylesheet, reference.Url).ToString() }));
            }
        }

        references.ShouldNotBeEmpty("the page links at least its stylesheet, so the extractor ran");
        ResourceReferences.NotAllowed(references, policy, page)
            .Select(reference => $"{reference.Source} {reference.Directive} {reference.Url}")
            .ShouldBeEmpty($"{path} loads something its policy does not allow; add it to ThirdPartyAllowlist with a purpose, or remove it");
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Pages_have_no_inline_script_style_or_event_handlers(string path)
    {
        var html = await site.Web.GetStringAsync(path, TestContext.Current.CancellationToken);

        InlineScript().IsMatch(html).ShouldBeFalse($"{path} has an inline <script>, which the policy blocks");
        html.ShouldNotContain("<style", Case.Insensitive, $"{path} has a <style> block, which the policy blocks");
        StyleAttribute().IsMatch(html).ShouldBeFalse($"{path} has a style attribute, which the policy blocks");
        EventHandler().IsMatch(html).ShouldBeFalse($"{path} has an inline event handler, which the policy blocks");
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : string.Empty;

    // Data blocks such as JSON-LD do not execute, so the policy does not block them.
    [GeneratedRegex("<script\\b(?![^>]*\\ssrc=)(?![^>]*\\stype=[\"']?application/(ld\\+)?json)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();

    [GeneratedRegex("<[a-z][^>]*\\sstyle=", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex("<[a-z][^>]*\\son[a-z]+=", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandler();
}

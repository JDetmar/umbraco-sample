using System.Text.RegularExpressions;
using Shouldly;

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
        var directives = Header(response, "Content-Security-Policy")
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(' ', 2))
            .ToDictionary(part => part[0], part => part.Length > 1 ? part[1].Split(' ') : []);

        foreach (Match reference in ResourceReference().Matches(html))
        {
            var directive = reference.Groups["tag"].Value.ToLowerInvariant() switch
            {
                "script" => "script-src",
                "img" or "source" => "img-src",
                "iframe" => "frame-src",
                "link" => "style-src",
                _ => "default-src",
            };

            foreach (var url in Urls(reference.Groups["url"].Value, reference.Groups["attr"].Value))
            {
                // Protocol-relative URLs load from another host. On Linux, "/path" parses as an absolute file URI,
                // so only http and https count as absolute here.
                var candidate = url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url;
                if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    || uri.Host == site.Web.BaseAddress!.Host)
                {
                    continue; // Relative, data: or same-site: allowed by 'self' (and data: for images).
                }

                var origin = uri.GetLeftPart(UriPartial.Authority);
                directives[directive].ShouldContain(origin, $"{path} loads {url} from an origin the allowlist does not approve for {directive}");
            }
        }
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

    private static IEnumerable<string> Urls(string value, string attribute) =>
        attribute.Equals("srcset", StringComparison.OrdinalIgnoreCase)
            ? value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(candidate => candidate.Split(' ')[0])
            : [value];

    // Elements that make the browser fetch something; anchors are navigation, not loads.
    [GeneratedRegex("<(?<tag>script|img|source|iframe|link|embed|object|video|audio)\\b[^>]*?\\s(?<attr>src|srcset|href|data)=\"(?<url>[^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceReference();

    [GeneratedRegex("<script\\b(?![^>]*\\ssrc=)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();

    [GeneratedRegex("<[a-z][^>]*\\sstyle=", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex("<[a-z][^>]*\\son[a-z]+=", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandler();
}

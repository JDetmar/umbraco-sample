using Shouldly;
using UmbracoSample.Tests.Support;
using UmbracoSample.Web.Security;

namespace UmbracoSample.Web.Tests.Security;

/// <summary>
/// Proves the oracle behind the integration test "pages load nothing the policy does not allow" (TST-10):
/// each hostile snippet must be reported, and each harmless one must not.
/// </summary>
public sealed class ResourceReferencesTests
{
    private static readonly Uri Page = new("http://localhost:5000/page/");
    private static readonly string Policy = SecurityHeaders.ContentSecurityPolicy(ThirdPartyAllowlist.Origins);

    [Theory]
    [InlineData("<script src=\"https://tracker.example/t.js\"></script>")]
    [InlineData("<script src='https://tracker.example/t.js'></script>")]
    [InlineData("<script src=https://tracker.example/t.js></script>")]
    [InlineData("<SCRIPT SRC=\"//tracker.example/t.js\"></SCRIPT>")]
    [InlineData("<img src=\"/x.png\" srcset=\"/x.png 1x, https://tracker.example/a.png 2x\">")]
    [InlineData("<img alt=\"x\" src=\"https://tracker.example/pixel.gif\" />")]
    [InlineData("<img src=\"https&#58;//tracker.example/pixel.gif\">")]
    [InlineData("<video poster=\"https://tracker.example/p.jpg\"></video>")]
    [InlineData("<video><track src=\"https://tracker.example/c.vtt\"></video>")]
    [InlineData("<input type=\"image\" src=\"https://tracker.example/b.png\">")]
    [InlineData("<form action=\"https://collector.example/submit\"></form>")]
    [InlineData("<button formaction=\"https://collector.example/submit\">Go</button>")]
    [InlineData("<iframe src=\"https://ads.example/frame\"></iframe>")]
    [InlineData("<object data=\"https://ads.example/x.swf\"></object>")]
    [InlineData("<base href=\"https://evil.example/\">")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0; url=https://evil.example/\">")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://fonts.example/css\">")]
    [InlineData("<link rel=\"preload\" as=\"script\" href=\"https://cdn.example/a.js\">")]
    [InlineData("<link rel=\"icon\" href=\"https://cdn.example/favicon.ico\">")]
    [InlineData("<link rel=\"preconnect\" href=\"https://tracker.example\">")]
    [InlineData("<img src=\"http://localhost:9999/other-port.png\">")]
    [InlineData("<img src=\"https://localhost:5000/other-scheme.png\">")]
    [InlineData("<script src=\"data:text/javascript,alert(1)\"></script>")]
    [InlineData("<img src=\"javascript:alert(1)\">")]
    public void Hostile_markup_is_reported(string html)
    {
        ResourceReferences.NotAllowed(ResourceReferences.InHtml(html), Policy, Page).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("<script src=\"/dist/site.js\"></script>")]
    [InlineData("<img src=\"x.png\" srcset=\"x.png 1x, /y.png 2x\">")]
    [InlineData("<img src=\"http://localhost:5000/abs.png\">")]
    [InlineData("<img src=\"data:image/png;base64,AAAA\">")]
    [InlineData("<link rel=\"stylesheet\" href=\"/dist/site.css?v=abc\">")]
    [InlineData("<link rel=\"canonical\" href=\"https://www.example.com/page/\">")]
    [InlineData("<link rel=\"alternate\" hreflang=\"fr\" href=\"https://fr.example.com/\">")]
    [InlineData("<a href=\"https://elsewhere.example/\">A link is navigation</a>")]
    [InlineData("<form action=\"/search\"></form>")]
    public void Same_site_and_navigation_markup_is_allowed(string html)
    {
        ResourceReferences.NotAllowed(ResourceReferences.InHtml(html), Policy, Page).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("@import url(\"https://fonts.example/css\");")]
    [InlineData("@import 'https://fonts.example/css';")]
    [InlineData(".x{background-image:url(https://tracker.example/p.png)}")]
    [InlineData("@font-face{font-family:x;src:url('https://fonts.example/x.woff2') format('woff2')}")]
    public void Hostile_stylesheets_are_reported(string css)
    {
        ResourceReferences.NotAllowed(ResourceReferences.InCss(css), Policy, Page).ShouldNotBeEmpty();
    }

    [Fact]
    public void Font_urls_are_checked_against_font_src_and_others_against_img_src()
    {
        var references = ResourceReferences.InCss("@font-face{src:url(/f.woff2)}.x{background:url(/i.png)}");

        references.Select(reference => (reference.Directive, reference.Url)).ShouldBe([("font-src", "/f.woff2"), ("img-src", "/i.png")]);
    }

    [Fact]
    public void An_allowlisted_origin_is_allowed_only_for_its_directive()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy([new ThirdPartyOrigin("https://cdn.example", "Test", new HashSet<ResourceKind> { ResourceKind.Script })]);

        ResourceReferences.NotAllowed(ResourceReferences.InHtml("<script src=\"https://cdn.example/a.js\"></script>"), policy, Page).ShouldBeEmpty();
        ResourceReferences.NotAllowed(ResourceReferences.InHtml("<img src=\"https://cdn.example/a.png\">"), policy, Page).ShouldNotBeEmpty();
    }
}

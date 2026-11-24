using Shouldly;
using UmbracoSample.Web.Components;

namespace UmbracoSample.Web.Tests.Components;

public sealed class LinkAndImageRuleTests
{
    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com/")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("tel:+15550100")]
    [InlineData("/relative/path/")]
    [InlineData("#section")]
    [InlineData("?page=2")]
    public void Safe_urls_are_kept(string url) =>
        LinkModel.TryCreate("Text", url, target: null).ShouldNotBeNull().Url.ShouldBe(url);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("vbscript:msgbox")]
    [InlineData("//evil.example/path")]
    [InlineData("file:///etc/passwd")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unsafe_or_missing_urls_are_dropped(string? url) =>
        LinkModel.TryCreate("Text", url, target: null).ShouldBeNull();

    [Fact]
    public void Missing_link_text_falls_back_to_the_url() =>
        LinkModel.TryCreate(" ", "https://example.com/", null).ShouldNotBeNull().Text.ShouldBe("https://example.com/");

    [Theory]
    [InlineData("_blank", true)]
    [InlineData("_BLANK", true)]
    [InlineData("_self", false)]
    [InlineData(null, false)]
    public void Only_blank_target_opens_a_new_tab(string? target, bool expected) =>
        LinkModel.TryCreate("Text", "/", target).ShouldNotBeNull().OpensInNewTab.ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Image_without_alt_text_is_not_rendered(string? alt) =>
        ImageModel.TryCreate("/media/a.jpg", alt).ShouldBeNull();

    [Fact]
    public void Image_without_url_is_not_rendered() =>
        ImageModel.TryCreate(null, "Alt").ShouldBeNull();

    [Fact]
    public void Image_with_alt_text_is_rendered_with_trimmed_alt() =>
        ImageModel.TryCreate("/media/a.jpg", "  A cat  ").ShouldBe(new ImageModel("/media/a.jpg", "A cat"));
}

using CsCheck;
using Shouldly;
using UmbracoSample.Domain.Redirects;

namespace UmbracoSample.Domain.Tests.Redirects;

public sealed class SitePathTests
{
    // Characters that make paths interesting: separators, dots, case, encoding and the characters the parser rejects.
    private static readonly Gen<string> PathLike = Gen.String[Gen.Char["/aAbB.-_%2f?#\\, \t\u0000é"], 0, 24];

    private static readonly Gen<string> ValidSegment = Gen.String[Gen.Char["abcxyzABC0123-_~%"], 1, 8];

    private static readonly Gen<string> ValidPath = ValidSegment.Array[1, 4].Select(segments => "/" + string.Join('/', segments));

    [Theory]
    [InlineData("/about", "/about")]
    [InlineData("/About/", "/about")]
    [InlineData("/a/b///", "/a/b")]
    [InlineData("/", "/")]
    public void Valid_paths_are_normalised(string text, string expected)
    {
        SitePath.TryParse(text, out var path, out _).ShouldBeTrue();
        path.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("about")]
    [InlineData("//evil.example/x")]
    [InlineData("///")]
    [InlineData("/a//b")]
    [InlineData("/a/../b")]
    [InlineData("/a/./b")]
    [InlineData("/a?b=1")]
    [InlineData("/a#top")]
    [InlineData("/a\\b")]
    [InlineData("/a b")]
    [InlineData("/a,b")]
    [InlineData("/a\tb")]
    [InlineData("https://example.com/a")]
    public void Invalid_paths_are_rejected_with_a_reason(string? text)
    {
        SitePath.TryParse(text, out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parsing_never_throws_and_every_parsed_path_meets_the_invariants()
    {
        Gen.OneOf(PathLike, Gen.String).Sample(text =>
        {
            if (!SitePath.TryParse(text, out var path, out _))
            {
                return;
            }

            var value = path.Value;
            value.ShouldStartWith("/");
            value.ShouldBe(value.ToLowerInvariant());
            (value == "/" || !value.EndsWith('/')).ShouldBeTrue();
            value.ShouldNotContain("//");
            value.IndexOfAny(['?', '#', '\\', ',', ' ']).ShouldBe(-1);
            value.Split('/').Skip(1).ShouldAllBe(segment => value == "/" || (segment.Length > 0 && segment != "." && segment != ".."));
        });
    }

    [Fact]
    public void Normalising_is_idempotent()
    {
        Gen.OneOf(PathLike, ValidPath).Sample(text =>
        {
            if (SitePath.TryParse(text, out var once, out _))
            {
                SitePath.TryParse(once.Value, out var twice, out _).ShouldBeTrue();
                twice.ShouldBe(once);
            }
        });
    }

    [Fact]
    public void Case_and_trailing_slashes_do_not_change_a_path()
    {
        Gen.Select(ValidPath, Gen.Int[0, 3], Gen.Bool).Sample((text, slashes, upper) =>
        {
            var variant = (upper ? text.ToUpperInvariant() : text) + new string('/', slashes);
            SitePath.TryParse(text, out var original, out _).ShouldBeTrue();
            SitePath.TryParse(variant, out var parsed, out _).ShouldBeTrue();
            parsed.ShouldBe(original);
        });
    }
}

using System.Text;
using CsCheck;
using Shouldly;
using UmbracoSample.Domain.Redirects;

namespace UmbracoSample.Domain.Tests.Redirects;

public sealed class RedirectMapTests
{
    private static readonly string[] Reserved = ["/umbraco", "/health"];

    private static readonly Gen<string> Segment = Gen.String[Gen.Char["abcdefghij0123456789-"], 1, 6];

    private static readonly Gen<string> Path = Segment.Array[1, 3].Select(segments => "/" + string.Join('/', segments));

    private static readonly Gen<string> Kind = Gen.Bool.Select(permanent => permanent ? "permanent" : "temporary");

    /// <summary>A valid map: distinct sources ("old" paths) and targets ("new" paths or https URLs) that are never sources.</summary>
    private static readonly Gen<(string Source, string Target, string Kind)[]> ValidRules =
        Gen.Select(Path, Gen.Bool, Path, Kind)
            .Select((source, external, target, kind) => (Source: "/old" + source, Target: external ? "https://example.com" + target : "/new" + target, Kind: kind))
            .Array[0, 12]
            .Select(rules => rules.DistinctBy(rule => rule.Source).ToArray());

    private static readonly Gen<string> CsvNoise = Gen.String[Gen.Char["/,#abc?\\\r\n \"permanenttemporary:sh"], 0, 120];

    private static string Csv(IEnumerable<(string Source, string Target, string Kind)> rules)
    {
        var csv = new StringBuilder(RedirectMap.Header).Append('\n');
        foreach (var (source, target, kind) in rules)
        {
            csv.Append(source).Append(',').Append(target).Append(',').Append(kind).Append('\n');
        }

        return csv.ToString();
    }

    [Fact]
    public void A_valid_map_parses_and_every_source_finds_its_rule_in_any_case_or_with_a_trailing_slash()
    {
        ValidRules.Sample(rules =>
        {
            var (map, errors) = RedirectMap.Parse(Csv(rules), Reserved);

            errors.ShouldBeEmpty();
            map!.Rules.Count.ShouldBe(rules.Length);
            foreach (var (source, target, kind) in rules)
            {
                foreach (var variant in new[] { source, source.ToUpperInvariant(), source + "/" })
                {
                    var rule = map.Find(variant).ShouldNotBeNull();
                    rule.Target.Location.ShouldBe(target);
                    rule.Kind.ShouldBe(kind == "permanent" ? RedirectKind.Permanent : RedirectKind.Temporary);
                }
            }
        }, iter: 2_000);
    }

    /// <summary>The same path as written differently: case, trailing slash, query, fragment or percent-encoding.</summary>
    private static string Variant(string path, int i) => (i % 5) switch
    {
        0 => path.ToUpperInvariant(),
        1 => path + "/",
        2 => path + "?ref=old",
        3 => path + "#top",
        _ => path.Replace("o", "%6F", StringComparison.Ordinal),
    };

    [Fact]
    public void A_target_that_is_another_source_written_differently_is_still_a_chain()
    {
        for (var i = 0; i < 5; i++)
        {
            var (map, errors) = RedirectMap.Parse($"source,target,kind\n/a,{Variant("/old", i)},permanent\n/old,/new,permanent\n", Reserved);

            map.ShouldBeNull($"variant {Variant("/old", i)}");
            string.Join("; ", errors).ShouldContain("itself redirected");
        }
    }

    [Theory]
    [InlineData("/healthy")]
    [InlineData("/umbraco-old")]
    [InlineData("/health-insurance/claims")]
    public void Reserved_prefixes_match_whole_segments_only(string source)
    {
        RedirectMap.Parse($"source,target,kind\n{source},/x,permanent\n", Reserved).Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/x y")]
    [InlineData("/x\ty")]
    [InlineData("/naïve")]
    [InlineData("https://exämple.com/")]
    [InlineData("/x\u0001")]
    [InlineData("https://example.com/x y")]
    [InlineData("https://example.com/x\ty")]
    [InlineData("https://example.com/x\u0001")]
    [InlineData("https://example.com/x\u007f")]
    [InlineData("https://example.com/a\\b")]
    [InlineData("https://example.com/a\"b")]
    public void Targets_must_be_ascii_without_whitespace_or_control_characters(string target)
    {
        var (map, errors) = RedirectMap.Parse($"source,target,kind\n/a,{target},permanent\n", Reserved);

        map.ShouldBeNull();
        string.Join("; ", errors).ShouldContain("target");
    }

    [Fact]
    public void An_empty_target_is_rejected()
    {
        var (map, errors) = RedirectMap.Parse("source,target,kind\n/a,,permanent\n", Reserved);

        map.ShouldBeNull();
        string.Join("; ", errors).ShouldContain("empty");
    }

    [Fact]
    public void Find_decodes_the_request_path_only_once()
    {
        // The source /a%2541 means the literal path "/a%41". Kestrel has already decoded the request, so decoding again would turn it into "/aA".
        var (map, errors) = RedirectMap.Parse("source,target,kind\n/a%2541,/new,permanent\n", Reserved);

        errors.ShouldBeEmpty();
        map!.Find("/a%41").ShouldNotBeNull();
        map.Find("/aA").ShouldBeNull();
    }

    [Fact]
    public void A_character_above_the_basic_plane_survives_next_to_an_escape()
    {
        var (map, errors) = RedirectMap.Parse("source,target,kind\n/\U0001F600%2D,/new,permanent\n", Reserved);

        errors.ShouldBeEmpty();
        map!.Find("/\U0001F600-").ShouldNotBeNull();
        map.Find("/\uFFFD\uFFFD-").ShouldBeNull();
    }

    [Fact]
    public void Target_length_is_limited_at_the_boundary()
    {
        RedirectTarget.TryParse("/" + new string('a', SitePath.MaxLength - 1), out _, out _).ShouldBeTrue();
        RedirectTarget.TryParse("/" + new string('a', SitePath.MaxLength), out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("/n", "?x=1", "/n?x=1")]
    [InlineData("/n#top", "?x=1", "/n?x=1#top")]
    [InlineData("/n?a=1#top", "?x=1", "/n?a=1#top")]
    [InlineData("/n#q?", "?x=1", "/n?x=1#q?")]
    [InlineData("/n", "", "/n")]
    [InlineData("/n", "?", "/n")]
    [InlineData("/n", "x=1", "/n?x=1")]
    [InlineData("/n#top", "x=1", "/n?x=1#top")]
    [InlineData("https://example.com/p#s", "?x=1", "https://example.com/p?x=1#s")]
    public void The_request_query_goes_before_any_fragment(string target, string query, string expected)
    {
        RedirectTarget.TryParse(target, out var parsed, out _).ShouldBeTrue();
        parsed.LocationFor(query).ShouldBe(expected);
    }

    [Fact]
    public void A_byte_order_mark_before_the_header_is_ignored()
    {
        RedirectMap.Parse("\uFEFFsource,target,kind\n/a,/b,permanent\n", Reserved).Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Percent_encoded_sources_match_the_request_path_the_server_decodes()
    {
        var (map, _) = RedirectMap.Parse("source,target,kind\n/caf%C3%A9,/b,permanent\n", Reserved);

        map!.Find("/café").ShouldNotBeNull();
        map.Find("/CAFÉ/").ShouldNotBeNull();
    }

    [Fact]
    public void Parsing_never_throws_and_a_parsed_map_is_always_flat()
    {
        // Mix valid rules with noise, and with rules whose targets reuse sources, to reach chains and loops.
        var gen = Gen.Select(ValidRules, CsvNoise, Gen.Int[0, 3]).Select((rules, noise, mode) => mode switch
        {
            0 => Csv(rules) + noise,
            1 => Csv(rules.Select((rule, i) => (rule.Source, Variant(rules[(i + 1) % rules.Length].Source, i), rule.Kind))),
            2 => noise,
            _ => Csv(rules),
        });

        gen.Sample(text =>
        {
            var (map, errors) = RedirectMap.Parse(text, Reserved);

            (map is null).ShouldBe(errors.Count > 0);
            if (map is null)
            {
                return;
            }

            var sources = map.Rules.Select(rule => rule.Source).ToHashSet();
            sources.Count.ShouldBe(map.Rules.Count, "sources are unique");
            map.Rules.ShouldAllBe(rule => rule.Target.SitePath == null || !sources.Contains(rule.Target.SitePath.Value), "no rule targets another rule's source");
        }, iter: 5_000);
    }

    [Theory]
    [InlineData("/old,/new,permanent\n", "header")]
    [InlineData("source,target,kind\n/old,/new\n", "three fields")]
    [InlineData("source,target,kind\n/old,/new,forever\n", "kind")]
    [InlineData("source,target,kind\n/old,/old,permanent\n", "itself")]
    [InlineData("source,target,kind\n/Old,/x,permanent\n/old/,/y,permanent\n", "already mapped on line 2")]
    [InlineData("source,target,kind\n/a,/b,permanent\n/b,/c,permanent\n", "itself redirected on line 3")]
    [InlineData("source,target,kind\n/a,/b,permanent\n/b,/a,permanent\n", "itself redirected")]
    [InlineData("source,target,kind\n/umbraco/login,/x,permanent\n", "reserved")]
    [InlineData("source,target,kind\n/health,/x,permanent\n", "reserved")]
    [InlineData("source,target,kind\n/,/x,permanent\n", "home page")]
    [InlineData("source,target,kind\n/a,javascript:alert(1),permanent\n", "https")]
    [InlineData("source,target,kind\n/a,http://example.com/,permanent\n", "https")]
    [InlineData("source,target,kind\n/a,//evil.example/,permanent\n", "single /")]
    [InlineData("source,target,kind\n/a,https://user:pw@example.com/,permanent\n", "credentials")]
    [InlineData("", "no header")]
    public void Invalid_maps_report_why(string csv, string expected)
    {
        var (map, errors) = RedirectMap.Parse(csv, Reserved);

        map.ShouldBeNull();
        string.Join("; ", errors).ShouldContain(expected);
    }

    [Fact]
    public void Comments_blank_lines_and_windows_line_endings_are_accepted()
    {
        var (map, errors) = RedirectMap.Parse("# legacy URLs\r\nsource,target,kind\r\n\r\n# moved\r\n/old,/new?ref=old,temporary\r\n", Reserved);

        errors.ShouldBeEmpty();
        var rule = map!.Find("/OLD/").ShouldNotBeNull();
        rule.Target.Location.ShouldBe("/new?ref=old");
        rule.Target.HasQuery.ShouldBeTrue();
        rule.Kind.ShouldBe(RedirectKind.Temporary);
        rule.Line.ShouldBe(5);
    }

    [Fact]
    public void Requests_that_are_not_valid_paths_find_nothing()
    {
        var (map, _) = RedirectMap.Parse("source,target,kind\n/old,/new,permanent\n", Reserved);

        map!.Find(null).ShouldBeNull();
        map.Find("/old/../old").ShouldBeNull();
        map.Find("/elsewhere").ShouldBeNull();
    }
}

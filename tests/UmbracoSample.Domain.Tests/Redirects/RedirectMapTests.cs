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
        });
    }

    [Fact]
    public void Parsing_never_throws_and_a_parsed_map_is_always_flat()
    {
        // Mix valid rules with noise, and with rules whose targets reuse sources, to reach chains and loops.
        var gen = Gen.Select(ValidRules, CsvNoise, Gen.Int[0, 3]).Select((rules, noise, mode) => mode switch
        {
            0 => Csv(rules) + noise,
            1 => Csv(rules.Select((rule, i) => (rule.Source, rules[(i + 1) % rules.Length].Source, rule.Kind))),
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
        });
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

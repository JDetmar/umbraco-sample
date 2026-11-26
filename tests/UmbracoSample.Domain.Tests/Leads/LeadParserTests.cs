using System.Globalization;
using CsCheck;
using Shouldly;
using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Domain.Tests.Leads;

/// <summary>Threat model 0001: field injection and the D3 limits (ES-021). Properties with CsCheck (ES-002, ES-008).</summary>
public sealed class LeadParserTests
{
    private static LeadInput Valid => new("Ada Lovelace", "ada@example.com", "Analytical Engines", "Hello\r\nthere", false, "2026-11");

    private static readonly Gen<string> Hostile = Gen.String[Gen.Char["aZ <>\"'&@.\r\n\t\u0000\u0007\u001b\u007f\u0085\u200b\u200e\u202e\u2066\u2028\ufeff\ud800\u00e9\u6f22"], 0, 40];

    [Fact]
    public void A_valid_post_becomes_a_lead()
    {
        var result = LeadParser.Parse(Valid, TestRules.Default);

        result.IsValid.ShouldBeTrue();
        result.Lead!.Email.Value.ShouldBe("ada@example.com");
        result.Lead.Message.ShouldBe("Hello\nthere");
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Missing_required_fields_are_reported_per_field()
    {
        var result = LeadParser.Parse(new LeadInput(" ", null, null, null, false, null), TestRules.Default);

        result.IsValid.ShouldBeFalse();
        result.Errors.Keys.ShouldBe(["name", "email", "consentVersion"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("name", 100)]
    [InlineData("company", 100)]
    [InlineData("message", 2000)]
    public void A_field_at_its_limit_passes_and_one_over_is_rejected_not_truncated(string field, int limit)
    {
        LeadInput With(string value) => field switch
        {
            "name" => Valid with { Name = value },
            "company" => Valid with { Company = value },
            _ => Valid with { Message = value },
        };

        LeadParser.Parse(With(new string('a', limit)), TestRules.Default).IsValid.ShouldBeTrue();
        var over = LeadParser.Parse(With(new string('a', limit + 1)), TestRules.Default);
        over.IsValid.ShouldBeFalse();
        over.Errors.ShouldContainKey(field);
    }

    [Fact]
    public void Optional_fields_that_are_blank_become_null()
    {
        var lead = LeadParser.Parse(Valid with { Company = " \t ", Message = "\u200b" }, TestRules.Default).Lead!;

        lead.Company.ShouldBeNull();
        lead.Message.ShouldBeNull();
    }

    [Fact]
    public void Limits_count_characters_after_normalising()
    {
        // 100 visible characters padded with zero-width ones still fit the name limit.
        LeadParser.Parse(Valid with { Name = new string('a', 100) + "​⁠﻿" }, TestRules.Default).IsValid.ShouldBeTrue();

        // 999 lines ending in CRLF are 2,998 characters posted but 1,999 after normalising (each CRLF becomes "\n").
        var crlf = string.Concat(Enumerable.Repeat("a\r\n", 999)) + "a";
        crlf.Length.ShouldBeGreaterThan(2000);
        var result = LeadParser.Parse(Valid with { Message = crlf }, TestRules.Default);
        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => $"{e.Key} {e.Value}")));
        result.Lead!.Message!.Length.ShouldBe(1999);
    }

    [Fact]
    public void Configured_limits_replace_the_defaults()
    {
        var rules = TestRules.Default with { Limits = new LeadLimits(name: 10) };

        LeadParser.Parse(Valid with { Name = new string('a', 10) }, rules).IsValid.ShouldBeTrue();
        LeadParser.Parse(Valid with { Name = new string('a', 11) }, rules).Errors.ShouldContainKey("name");
        Should.Throw<ArgumentOutOfRangeException>(() => new LeadLimits(email: 255));
        Should.Throw<ArgumentOutOfRangeException>(() => new LeadLimits(message: 0));
    }

    [Theory]
    [InlineData("v999")]
    [InlineData("2026-12")]
    [InlineData("<b>v1</b>")]
    [InlineData("")]
    public void A_consent_version_the_site_did_not_publish_is_rejected(string version)
    {
        var result = LeadParser.Parse(Valid with { ConsentVersion = version }, TestRules.Default);

        result.Errors.ShouldContainKey("consentVersion");
    }

    [Fact]
    public void Parsed_leads_hold_only_plain_text_within_each_field_limit() =>
        Gen.Select(Field(LeadLimits.Default.Name), Field(LeadLimits.Default.Company), Field(LeadLimits.Default.Message), ValidEmail, Gen.Bool, Gen.Int[0, 3])
            .Sample((name, company, message, email, consent, version) =>
            {
                var input = new LeadInput(name, email, company, message, consent, version < 2 ? "2026-11" : version == 2 ? "v1" : "forged");
                var result = LeadParser.Parse(input, TestRules.Default);

                if (version == 3)
                {
                    result.Errors.ShouldContainKey("consentVersion");
                    return;
                }

                var expectValid = Fits(name, LeadLimits.Default.Name, required: true) && Fits(company, LeadLimits.Default.Company) && Fits(message, LeadLimits.Default.Message, multiline: true);
                result.IsValid.ShouldBe(expectValid, string.Join("; ", result.Errors.Select(e => $"{e.Key} {e.Value}")));
                if (result.Lead is not { } lead)
                {
                    return;
                }

                lead.Name.Length.ShouldBeInRange(1, LeadLimits.Default.Name);
                (lead.Company?.Length ?? 0).ShouldBeLessThanOrEqualTo(LeadLimits.Default.Company);
                (lead.Message?.Length ?? 0).ShouldBeLessThanOrEqualTo(LeadLimits.Default.Message);
                lead.Name.ShouldNotContain(c => c == '\n' || IsForbidden(c));
                lead.Company?.ShouldNotContain(c => c == '\n' || IsForbidden(c));
                lead.Message?.ShouldNotContain(c => c != '\n' && IsForbidden(c));
                lead.MarketingConsent.ShouldBe(consent);
            }, iter: 2000);

    // Mostly plain text, with hostile characters mixed in, and lengths that straddle the field's limit.
    private static Gen<string> Field(int limit) =>
        Gen.Frequency(
            (1, Hostile),
            (3, Gen.Int[limit - 5, limit + 5].SelectMany(n => Gen.String[Gen.Char["abc XYZ​\r\n"], n, n])));

    private static readonly Gen<string> ValidEmail =
        Gen.Select(Gen.String[Gen.Char["abcxyz019+_-"], 1, 20], Gen.String[Gen.Char["abcxyz019+_-"], 0, 20], Gen.String[Gen.Char["abcdefgh"], 1, 12])
            .Select((first, second, host) => second.Length == 0 ? $"{first}@{host}.example" : $"{first}.{second}@{host}.example");

    private static bool Fits(string text, int limit, bool required = false, bool multiline = false)
    {
        var length = PlainText.Normalise(text, multiline).Length;
        return length <= limit && (!required || length > 0);
    }

    [Fact]
    public void Unassigned_and_private_use_characters_outside_the_basic_plane_are_removed()
    {
        // U+1FFFE is a noncharacter (unassigned), U+E0001 a tag (format), U+F0000 private use; a lone surrogate is dropped.
        PlainText.Normalise("a\U0001FFFEb\U000E0001c\U000F0000d\ud800e", multiline: false).ShouldBe("abcde");
        PlainText.Normalise("a\U0001F600b", multiline: false).ShouldBe("a\U0001F600b");
    }

    [Fact]
    public void Parsing_never_throws() =>
        Gen.Select(Gen.OneOf(Hostile, Gen.String), Gen.OneOf(Hostile, Gen.String), Gen.Bool).Sample((a, b, consent) =>
            Should.NotThrow(() => LeadParser.Parse(new LeadInput(a, b, a, b, consent, b), TestRules.Default)));

    [Fact]
    public void Normalising_twice_changes_nothing() =>
        Gen.Select(Hostile, Gen.Bool).Sample((text, multiline) =>
        {
            var once = PlainText.Normalise(text, multiline);
            PlainText.Normalise(once, multiline).ShouldBe(once);
        });

    internal static bool IsForbidden(char c) =>
        c != '\n' && (char.IsSurrogate(c) is false && CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse);
}

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
        var result = LeadParser.Parse(Valid);

        result.IsValid.ShouldBeTrue();
        result.Lead!.Email.Value.ShouldBe("ada@example.com");
        result.Lead.Message.ShouldBe("Hello\nthere");
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Missing_required_fields_are_reported_per_field()
    {
        var result = LeadParser.Parse(new LeadInput(" ", null, null, null, false, null));

        result.IsValid.ShouldBeFalse();
        result.Errors.Keys.ShouldBe(["name", "email", "consentVersion"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("name", LeadLimits.Name)]
    [InlineData("company", LeadLimits.Company)]
    [InlineData("message", LeadLimits.Message)]
    public void A_field_at_its_limit_passes_and_one_over_is_rejected_not_truncated(string field, int limit)
    {
        LeadInput With(string value) => field switch
        {
            "name" => Valid with { Name = value },
            "company" => Valid with { Company = value },
            _ => Valid with { Message = value },
        };

        LeadParser.Parse(With(new string('a', limit))).IsValid.ShouldBeTrue();
        var over = LeadParser.Parse(With(new string('a', limit + 1)));
        over.IsValid.ShouldBeFalse();
        over.Errors.ShouldContainKey(field);
    }

    [Fact]
    public void Optional_fields_that_are_blank_become_null()
    {
        var lead = LeadParser.Parse(Valid with { Company = " \t ", Message = "\u200b" }).Lead!;

        lead.Company.ShouldBeNull();
        lead.Message.ShouldBeNull();
    }

    [Fact]
    public void Parsed_text_never_holds_control_or_format_characters_and_stays_within_limits() =>
        Gen.Select(Hostile, Hostile, Hostile, Hostile).Sample((name, company, message, email) =>
        {
            var result = LeadParser.Parse(new LeadInput(name, email, company, message, false, "v1"));
            if (result.Lead is not { } lead)
            {
                return;
            }

            foreach (var text in new[] { lead.Name, lead.Company, lead.Message, lead.Email.Value })
            {
                text?.ShouldNotContain(c => IsForbidden(c) || (c == '\n' && !ReferenceEquals(text, lead.Message)));
                text?.Length.ShouldBeLessThanOrEqualTo(LeadLimits.Message);
            }

            lead.Name.Length.ShouldBeInRange(1, LeadLimits.Name);
        });

    [Fact]
    public void Parsing_never_throws() =>
        Gen.Select(Gen.OneOf(Hostile, Gen.String), Gen.OneOf(Hostile, Gen.String), Gen.Bool).Sample((a, b, consent) =>
            Should.NotThrow(() => LeadParser.Parse(new LeadInput(a, b, a, b, consent, b))));

    [Fact]
    public void Normalising_twice_changes_nothing() =>
        Gen.Select(Hostile, Gen.Bool).Sample((text, multiline) =>
        {
            var once = PlainText.Normalise(text, multiline);
            PlainText.Normalise(once, multiline).ShouldBe(once);
        });

    private static bool IsForbidden(char c) =>
        c != '\n' && (char.IsSurrogate(c) is false && CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse);
}

using CsCheck;
using Shouldly;
using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Domain.Tests.Leads;

public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("ada@example.com", "ada@example.com")]
    [InlineData("  Ada.Lovelace+leads@Example.CO.uk ", "Ada.Lovelace+leads@example.co.uk")]
    [InlineData("o'brien@sub-domain.example.org", "o'brien@sub-domain.example.org")]
    public void Common_work_addresses_parse_and_the_domain_is_lower_cased(string text, string expected)
    {
        EmailAddress.TryParse(text, out var email, out var error).ShouldBeTrue(error);
        email.Value.ShouldBe(expected);
    }

    [Fact]
    public void A_punycode_top_level_label_is_accepted()
    {
        EmailAddress.TryParse("ada@example.xn--p1ai", out var email, out var error).ShouldBeTrue(error);
        email.Value.ShouldBe("ada@example.xn--p1ai");
        EmailAddress.TryParse("ada@example.xn--", out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ada")]
    [InlineData("@example.com")]
    [InlineData("ada@")]
    [InlineData("ada@@example.com")]
    [InlineData("a@b@example.com")]
    [InlineData("ada@localhost")]
    [InlineData("ada@example.c")]
    [InlineData("ada@example.123")]
    [InlineData("ada@-example.com")]
    [InlineData("ada@example..com")]
    [InlineData(".ada@example.com")]
    [InlineData("ada.@example.com")]
    [InlineData("a..da@example.com")]
    [InlineData("\"ada\"@example.com")]
    [InlineData("ada@[127.0.0.1]")]
    [InlineData("adà@example.com")]
    [InlineData("ada@exämple.com")]
    [InlineData("ada lovelace@example.com")]
    [InlineData("ada@example.com\r\nBcc: x@example.com")]
    [InlineData("<script>@example.com")]
    [InlineData("ada\u200b@example.com")]
    [InlineData("ada@exam\u00adple.com")]
    public void Anything_else_is_refused(string text) =>
        EmailAddress.TryParse(text, out _, out var error).ShouldBeFalse(error);

    [Fact]
    public void Limits_hold_at_the_boundary()
    {
        var local64 = new string('a', LeadLimits.EmailLocalPart);
        EmailAddress.TryParse($"{local64}@example.com", out _, out _).ShouldBeTrue();
        EmailAddress.TryParse($"a{local64}@example.com", out _, out _).ShouldBeFalse();

        // 64 + "@" + 189 = 254 characters; one more character in the domain makes 255.
        string Address(int third) => $"{local64}@{new string('d', 63)}.{new string('d', 63)}.{new string('d', third)}.com";
        var at254 = Address(57);
        at254.Length.ShouldBe(LeadLimits.MaxEmail);
        EmailAddress.TryParse(at254, out _, out var error).ShouldBeTrue(error);

        var at255 = Address(58);
        at255.Length.ShouldBe(LeadLimits.MaxEmail + 1);
        EmailAddress.TryParse(at255, out _, out error).ShouldBeFalse();
        error.ShouldBe($"must be at most {LeadLimits.MaxEmail} characters");
    }

    [Fact]
    public void A_configured_lower_limit_applies_and_a_higher_one_cannot_raise_the_maximum()
    {
        EmailAddress.TryParse("ada@example.com", out _, out _, maxLength: 15).ShouldBeTrue();
        EmailAddress.TryParse("ada@example.com", out _, out _, maxLength: 14).ShouldBeFalse();
        var long255 = $"{new string('a', 64)}@{new string('d', 63)}.{new string('d', 63)}.{new string('d', 58)}.com";
        EmailAddress.TryParse(long255, out _, out _, maxLength: 1000).ShouldBeFalse();
    }

    [Fact]
    public void A_parsed_address_parses_again_to_itself() =>
        Gen.String[Gen.Char["abcAB09.+-_'@é \r"], 0, 30].Sample(text =>
        {
            if (EmailAddress.TryParse(text, out var email, out _))
            {
                EmailAddress.TryParse(email.Value, out var again, out _).ShouldBeTrue();
                again.ShouldBe(email);
                email.Value.ShouldNotContain(' ');
                email.Value.Count(c => c == '@').ShouldBe(1);
            }
        });
}

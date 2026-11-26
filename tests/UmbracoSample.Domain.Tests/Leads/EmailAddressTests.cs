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
    public void Anything_else_is_refused(string text) =>
        EmailAddress.TryParse(text, out _, out var error).ShouldBeFalse(error);

    [Fact]
    public void Limits_hold_at_the_boundary()
    {
        var local64 = new string('a', LeadLimits.EmailLocalPart);
        EmailAddress.TryParse($"{local64}@example.com", out _, out _).ShouldBeTrue();
        EmailAddress.TryParse($"a{local64}@example.com", out _, out _).ShouldBeFalse();

        var longDomain = string.Join('.', Enumerable.Repeat(new string('d', 60), 4)) + ".com";
        var atLimit = $"{new string('a', LeadLimits.Email - longDomain.Length - 1)}@{longDomain}";
        (atLimit.Length <= LeadLimits.Email).ShouldBeTrue();
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

using CsCheck;
using Shouldly;
using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Domain.Tests.Leads;

/// <summary>Threat model 0001: a lead without marketing consent never reaches a marketing list.</summary>
public sealed class LeadRoutingTests
{
    private static readonly PlatformList Sales = new("sales", "list-1", ListPurpose.Sales);
    private static readonly PlatformList Newsletter = new("newsletter", "list-2", ListPurpose.Marketing);

    [Theory]
    [InlineData(true, "newsletter")]
    [InlineData(false, null)]
    public void A_marketing_list_needs_consent(bool consent, string? expected) =>
        (LeadRouting.Choose(Newsletter, consent)?.Key).ShouldBe(expected);

    [Fact]
    public void Without_consent_a_lead_from_a_marketing_block_goes_to_no_list()
    {
        LeadRouting.Choose(Newsletter, marketingConsent: false).ShouldBeNull();
        LeadRouting.Choose(null, marketingConsent: true).ShouldBeNull();
    }

    [Fact]
    public void A_sales_list_is_used_with_or_without_consent()
    {
        LeadRouting.Choose(Sales, marketingConsent: false).ShouldBe(Sales);
        LeadRouting.Choose(Sales, marketingConsent: true).ShouldBe(Sales);
    }

    [Fact]
    public void No_choice_ever_puts_an_unconsented_lead_on_a_marketing_list()
    {
        var list = Gen.Select(Gen.String[1, 5], Gen.Enum<ListPurpose>()).Select((key, purpose) => new PlatformList(key, key, purpose));
        Gen.Select(list.Null(), Gen.Bool).Sample((block, consent) =>
        {
            var chosen = LeadRouting.Choose(block, consent);
            if (!consent)
            {
                chosen?.Purpose.ShouldBe(ListPurpose.Sales);
            }

            (chosen is null || ReferenceEquals(chosen, block)).ShouldBeTrue();
        });
    }
}

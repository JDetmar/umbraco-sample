using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Domain.Tests.Leads;

internal static class TestRules
{
    public static readonly LeadRules Default = new(LeadLimits.Default, new HashSet<string>(["2026-11", "v1"], StringComparer.Ordinal));
}

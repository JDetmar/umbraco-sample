namespace UmbracoSample.Domain.Leads;

public enum ListPurpose
{
    Sales,
    Marketing,
}

/// <summary>A marketing platform list from the configured allowlist. Editors pick a key; the request never names a list.</summary>
public sealed record PlatformList(string Key, string ListId, ListPurpose Purpose);

/// <summary>
/// Chooses the list a lead goes to (threat model 0001, tampering): a marketing list only with consent. Without consent a
/// lead goes to the block's sales list, or to no list. Inputs come from the published block and configuration only.
/// </summary>
public static class LeadRouting
{
    public static PlatformList? Choose(PlatformList? blockList, PlatformList? salesFallback, bool marketingConsent)
    {
        if (blockList is { Purpose: ListPurpose.Sales })
        {
            return blockList;
        }

        if (blockList is { Purpose: ListPurpose.Marketing } && marketingConsent)
        {
            return blockList;
        }

        return salesFallback is { Purpose: ListPurpose.Sales } ? salesFallback : null;
    }
}

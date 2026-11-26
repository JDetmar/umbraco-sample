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
/// lead goes to the block's sales list, or to no list. The list comes from the published block only, never the request.
/// </summary>
public static class LeadRouting
{
    public static PlatformList? Choose(PlatformList? blockList, bool marketingConsent) => blockList switch
    {
        { Purpose: ListPurpose.Sales } => blockList,
        { Purpose: ListPurpose.Marketing } when marketingConsent => blockList,
        _ => null,
    };
}

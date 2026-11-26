using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Leads;

/// <summary>
/// What a published Form block says, as the server reads it (threat model 0001, tampering): the list the block points
/// to, already resolved through the configured allowlist, and the consent text versions the block has shown. The request
/// names only the block, through the signed render token, and never a list.
/// </summary>
public sealed record LeadFormSettings(PlatformList? List, IReadOnlySet<string> ConsentVersions);

/// <summary>Finds a published Form block's settings. The host implements it over its CMS; an unknown block gives null.</summary>
public interface ILeadFormCatalog
{
    LeadFormSettings? Find(Guid blockKey);
}

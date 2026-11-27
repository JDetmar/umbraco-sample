using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Web.Leads;

/// <summary>
/// The site's side of the lead form (threat model 0001), bound from <c>Leads</c>. The form is off unless
/// <see cref="Enabled"/> is set; dev keeps it off until the edge exists (R2, DEV-010), and every deployed environment
/// also needs the shared Data Protection key ring first (R3).
/// </summary>
public sealed class LeadSiteOptions
{
    public const string Section = "Leads";

    public bool Enabled { get; set; }

    /// <summary>
    /// The allowlist of lists, keyed by the values of the block's List dropdown. Editors pick a key; only this
    /// configuration knows the platform's list ID (tampering).
    /// </summary>
    public Dictionary<string, LeadListOptions> Lists { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The consent wording per version, keyed by the values of the block's Consent version dropdown. The wording is fixed
    /// here, not in content, so the version the MAP stores always names the words the visitor saw (repudiation).
    /// </summary>
    public Dictionary<string, string> Consent { get; set; } = new(StringComparer.Ordinal);

    public string? Validate()
    {
        foreach (var (key, list) in Lists)
        {
            if (string.IsNullOrWhiteSpace(list.ListId) || list.Purpose is null)
            {
                return $"Leads:Lists:{key} needs a ListId and a Purpose (Sales or Marketing).";
            }
        }

        foreach (var (version, wording) in Consent)
        {
            if (string.IsNullOrWhiteSpace(wording) || version.Length > LeadLimits.ConsentVersion)
            {
                return $"Leads:Consent:{version} needs its wording, and a version of at most {LeadLimits.ConsentVersion} characters.";
            }
        }

        return null;
    }
}

public sealed class LeadListOptions
{
    public string? ListId { get; set; }

    public ListPurpose? Purpose { get; set; }
}

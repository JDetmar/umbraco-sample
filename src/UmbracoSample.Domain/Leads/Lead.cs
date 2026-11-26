namespace UmbracoSample.Domain.Leads;

/// <summary>What the visitor posted, as raw strings. Nothing here is trusted.</summary>
public sealed record LeadInput(string? Name, string? Email, string? Company, string? Message, bool MarketingConsent, string? ConsentVersion);

/// <summary>
/// A lead that passed <see cref="LeadParser"/>: every field is plain text within its limit, and the consent version is
/// one the site published (ES-021). The constructor is internal and there is no <c>with</c>, so the parser is the only
/// way to get one.
/// </summary>
public sealed class Lead
{
    internal Lead(string name, EmailAddress email, string? company, string? message, bool marketingConsent, string consentVersion)
    {
        Name = name;
        Email = email;
        Company = company;
        Message = message;
        MarketingConsent = marketingConsent;
        ConsentVersion = consentVersion;
    }

    public string Name { get; }

    public EmailAddress Email { get; }

    public string? Company { get; }

    public string? Message { get; }

    public bool MarketingConsent { get; }

    public string ConsentVersion { get; }
}

/// <summary>Either a lead or the errors per field, keyed by field name, so the form can show each one beside its input.</summary>
public sealed record LeadParseResult(Lead? Lead, IReadOnlyDictionary<string, string> Errors)
{
    public bool IsValid => Lead is not null;
}

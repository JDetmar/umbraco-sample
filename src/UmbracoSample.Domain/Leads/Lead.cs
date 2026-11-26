namespace UmbracoSample.Domain.Leads;

/// <summary>What the visitor posted, as raw strings. Nothing here is trusted.</summary>
public sealed record LeadInput(string? Name, string? Email, string? Company, string? Message, bool MarketingConsent, string? ConsentVersion);

/// <summary>A lead that passed <see cref="LeadParser"/>: every field is plain text within its limit (ES-021).</summary>
public sealed record Lead(string Name, EmailAddress Email, string? Company, string? Message, bool MarketingConsent, string ConsentVersion);

/// <summary>Either a lead or the errors per field, keyed by field name, so the form can show each one beside its input.</summary>
public sealed record LeadParseResult(Lead? Lead, IReadOnlyDictionary<string, string> Errors)
{
    public bool IsValid => Lead is not null;
}

namespace UmbracoSample.Domain.Leads;

/// <summary>
/// Parses a lead form post into a <see cref="Lead"/> (threat model 0001: field injection, D3 limits). Never throws.
/// Text is normalised with <see cref="PlainText"/> first, then checked against the limits, so a value is rejected,
/// never silently truncated.
/// </summary>
public static class LeadParser
{
    public static LeadParseResult Parse(LeadInput input, LeadRules rules)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);
        var limits = rules.Limits;
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        var name = PlainText.Normalise(input.Name, multiline: false);
        if (name.Length == 0)
        {
            errors["name"] = "is required";
        }
        else if (name.Length > limits.Name)
        {
            errors["name"] = $"must be at most {limits.Name} characters";
        }

        if (!EmailAddress.TryParse(input.Email, out var email, out var emailError, limits.Email))
        {
            errors["email"] = emailError;
        }

        var company = Optional(input.Company, multiline: false, limits.Company, "company", errors);
        var message = Optional(input.Message, multiline: true, limits.Message, "message", errors);

        var version = PlainText.Normalise(input.ConsentVersion, multiline: false);
        if (version.Length is 0 or > LeadLimits.ConsentVersion || !rules.ConsentVersions.Contains(version))
        {
            errors["consentVersion"] = "is not a known consent text version";
        }

        return errors.Count > 0
            ? new LeadParseResult(null, errors)
            : new LeadParseResult(new Lead(name, email!, company, message, input.MarketingConsent, version), errors);
    }

    private static string? Optional(string? raw, bool multiline, int limit, string field, Dictionary<string, string> errors)
    {
        var value = PlainText.Normalise(raw, multiline);
        if (value.Length > limit)
        {
            errors[field] = $"must be at most {limit} characters";
        }

        return value.Length == 0 ? null : value;
    }
}

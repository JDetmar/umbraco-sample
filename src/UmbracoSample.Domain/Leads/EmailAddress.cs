using System.Diagnostics.CodeAnalysis;

namespace UmbracoSample.Domain.Leads;

/// <summary>
/// A work email address in the common ASCII form: one "@", a dot-atom local part of at most 64 characters, and a domain
/// of at least two DNS labels with an alphabetic top-level label. The domain is lower-cased. Quoted local parts, IP
/// literals and internationalised addresses are refused on purpose: a marketing platform rarely accepts them, and a
/// narrow rule is easier to prove (ES-021). A top-level label is alphabetic or an IDNA A-label ("xn--"). Invisible
/// characters inside an address are refused rather than removed, so the address stored is the one the visitor typed.
/// The only way to get one is <see cref="TryParse"/>.
/// </summary>
public sealed record EmailAddress
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;

    public static bool TryParse(string? text, [NotNullWhen(true)] out EmailAddress? email, [NotNullWhen(false)] out string? error, int maxLength = LeadLimits.MaxEmail)
    {
        email = null;
        var value = PlainText.Normalise(text, multiline: false);
        if (value.Length == 0)
        {
            error = "is required";
            return false;
        }

        if (value.Length > Math.Min(maxLength, LeadLimits.MaxEmail))
        {
            error = $"must be at most {Math.Min(maxLength, LeadLimits.MaxEmail)} characters";
            return false;
        }

        if (!string.Equals(value, text!.Trim(), StringComparison.Ordinal))
        {
            // Normalising removed or replaced something inside the address: refuse it rather than store a different one.
            error = "must be an email address";
            return false;
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            error = "must be an email address";
            return false;
        }

        var local = value[..at];
        var domain = value[(at + 1)..];
        if (!IsLocalPart(local) || !IsDomain(domain))
        {
            error = "must be an email address";
            return false;
        }

        email = new EmailAddress(string.Concat(local, "@", domain.ToLowerInvariant()));
        error = null;
        return true;
    }

    private static bool IsLocalPart(string local)
    {
        if (local.Length > LeadLimits.EmailLocalPart || local[0] == '.' || local[^1] == '.' || local.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var c in local)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || "!#$%&'*+/=?^_`{|}~.-".Contains(c, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDomain(string domain)
    {
        var labels = domain.Split('.');
        if (labels.Length < 2 || domain.Length > 253)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }

            foreach (var c in label)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c == '-'))
                {
                    return false;
                }
            }
        }

        var top = labels[^1];
        return top.StartsWith("xn--", StringComparison.Ordinal) ? top.Length > 4 : top.Length >= 2 && top.All(char.IsAsciiLetter);
    }
}

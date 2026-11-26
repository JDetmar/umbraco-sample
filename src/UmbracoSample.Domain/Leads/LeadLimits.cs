namespace UmbracoSample.Domain.Leads;

/// <summary>
/// Field limits from threat model 0001, decision D3. The four field limits are configuration (D3) and default to the
/// decided numbers; anything longer is rejected, never truncated. Each limit counts characters after
/// <see cref="PlainText"/> normalisation. The email limit can be lowered but never raised above <see cref="MaxEmail"/>.
/// </summary>
public sealed record LeadLimits
{
    /// <summary>The longest address SMTP allows (RFC 5321 path limit less the angle brackets).</summary>
    public const int MaxEmail = 254;

    /// <summary>Fixed by RFC 5321, not policy.</summary>
    public const int EmailLocalPart = 64;

    /// <summary>A consent text version is an identifier, not visitor text.</summary>
    public const int ConsentVersion = 32;

    public LeadLimits(int name = 100, int email = MaxEmail, int company = 100, int message = 2000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(company);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(message);
        ArgumentOutOfRangeException.ThrowIfLessThan(email, 6);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(email, MaxEmail);
        Name = name;
        Email = email;
        Company = company;
        Message = message;
    }

    /// <summary>The numbers decided in D3.</summary>
    public static LeadLimits Default { get; } = new();

    public int Name { get; }

    public int Email { get; }

    public int Company { get; }

    public int Message { get; }
}

/// <summary>
/// What the server decides, never the request: the field limits, and the consent text versions the site has published.
/// A post naming any other version is rejected, so a forged version never reaches the marketing platform.
/// </summary>
public sealed record LeadRules(LeadLimits Limits, IReadOnlySet<string> ConsentVersions);

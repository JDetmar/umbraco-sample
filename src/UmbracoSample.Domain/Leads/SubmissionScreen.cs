namespace UmbracoSample.Domain.Leads;

public enum ScreenVerdict
{
    Accept,
    Honeypot,
    TooFast,
    Expired,
    FromTheFuture,
}

/// <summary>
/// Cheap bot screening before a lead is parsed (threat model 0001, automated abuse): the hidden honeypot field must be
/// empty, and the form must have been rendered between <see cref="MinimumAge"/> and <see cref="MaximumAge"/> ago. The
/// render time comes from the signed render token, which the web layer verifies; this only judges the time.
/// </summary>
public static class SubmissionScreen
{
    public static readonly TimeSpan MinimumAge = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(2);

    /// <summary>Allowed clock difference between replicas before a render time counts as in the future.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static ScreenVerdict Check(string? honeypot, DateTimeOffset renderedAt, DateTimeOffset now)
    {
        if (!string.IsNullOrEmpty(honeypot))
        {
            return ScreenVerdict.Honeypot;
        }

        var age = now - renderedAt;
        if (age < -ClockSkew)
        {
            return ScreenVerdict.FromTheFuture;
        }

        if (age < MinimumAge)
        {
            return ScreenVerdict.TooFast;
        }

        return age > MaximumAge ? ScreenVerdict.Expired : ScreenVerdict.Accept;
    }
}

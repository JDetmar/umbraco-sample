using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Leads;

/// <summary>
/// One lead to send. The idempotency key comes from the signed render token, so a resubmission of the same rendered form
/// carries the same key and the MAP keeps one lead (threat model 0001, section 5).
/// </summary>
public sealed record MapSubmission(Lead Lead, string ListId, string IdempotencyKey, DateTimeOffset SubmittedAt);

/// <summary>What happened to a submission, in the terms the visitor is told.</summary>
public enum MapOutcome
{
    /// <summary>The MAP holds the lead (new or already known): say thank you.</summary>
    Accepted,

    /// <summary>The MAP refused the lead as invalid; retrying will not help.</summary>
    Rejected,

    /// <summary>The MAP did not take the lead: ask the visitor to try again.</summary>
    Failed,

    /// <summary>The lead may or may not be in the MAP: ask the visitor not to resubmit for a few minutes.</summary>
    Unknown,

    /// <summary>Too many submissions in flight on this replica; refused without calling the MAP.</summary>
    Overloaded,
}

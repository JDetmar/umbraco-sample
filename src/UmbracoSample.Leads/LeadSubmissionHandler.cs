using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Leads;

/// <summary>One posted lead form, as raw strings. Nothing here is trusted.</summary>
public sealed record LeadFormPost(string? RenderToken, string? Honeypot, string? CaptchaResponse, LeadInput Input);

public enum LeadFormResultKind
{
    /// <summary>The MAP holds the lead: show the thank-you message.</summary>
    ThankYou,

    /// <summary>Some fields are invalid: show the errors beside them and keep the input.</summary>
    Invalid,

    /// <summary>The lead was not sent: ask the visitor to try again, keeping the input.</summary>
    TryAgain,

    /// <summary>The MAP refused this lead as invalid: resubmitting will not help, so offer another way to get in touch.</summary>
    NotAccepted,

    /// <summary>The lead may have been sent: ask the visitor not to resubmit for a few minutes.</summary>
    OutcomeUnknown,

    /// <summary>The post failed a bot check or its token did not read: ask the visitor to reload the page.</summary>
    Refused,
}

/// <summary>The result to render. <see cref="Errors"/> is set only for <see cref="LeadFormResultKind.Invalid"/>.</summary>
public sealed record LeadFormResult(LeadFormResultKind Kind, IReadOnlyDictionary<string, string> Errors)
{
    internal static LeadFormResult Of(LeadFormResultKind kind) => new(kind, new Dictionary<string, string>());
}

/// <summary>
/// Handles one lead form post (threat model 0001), after the host's endpoint has applied antiforgery, the body size limit
/// and the rate limit. In order: the render token must read; the honeypot and the token's age are screened; the CAPTCHA
/// is verified when it is on; the block must be published; the fields are parsed with the block's consent versions; the
/// list is chosen from the block, never the request; then the MAP is called with the token's idempotency key. Nothing is
/// stored, and nothing the visitor typed is logged.
/// <para>
/// When the host re-renders the form after <see cref="LeadFormResultKind.TryAgain"/>, <see cref="LeadFormResultKind.OutcomeUnknown"/>
/// or <see cref="LeadFormResultKind.Invalid"/>, it must put the posted render token back in the form, not issue a new
/// one: the token carries the idempotency key, and a new key on resubmission would let the MAP store the lead twice
/// (threat model 0001, section 5). Only a fresh page view issues a new token.
/// </para>
/// </summary>
public sealed partial class LeadSubmissionHandler(
    RenderTokenProtector tokens,
    ILeadFormCatalog catalog,
    IMapClient map,
    IOptions<LeadFormOptions> form,
    IOptions<CaptchaOptions> captcha,
    IEnumerable<ICaptchaVerifier> verifiers,
    TimeProvider time,
    ILogger<LeadSubmissionHandler> logger)
{
    public async Task<LeadFormResult> HandleAsync(LeadFormPost post, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(post);
        if (!tokens.TryRead(post.RenderToken, out var token))
        {
            return Refuse("token");
        }

        var now = time.GetUtcNow();
        var verdict = SubmissionScreen.Check(post.Honeypot, token.RenderedAt, now);
        if (verdict is not ScreenVerdict.Accept)
        {
            return Refuse(verdict.ToString());
        }

        if (captcha.Value.Enabled && !await verifiers.First().VerifyAsync(post.CaptchaResponse, cancellationToken).ConfigureAwait(false))
        {
            return Refuse("captcha");
        }

        if (catalog.Find(token.BlockKey) is not { } settings)
        {
            return Refuse("block");
        }

        var parsed = LeadParser.Parse(post.Input, new LeadRules(form.Value.Limits, settings.ConsentVersions));
        if (parsed.Lead is not { } lead)
        {
            LogResult(logger, LeadFormResultKind.Invalid, "fields");
            return new LeadFormResult(LeadFormResultKind.Invalid, parsed.Errors);
        }

        var list = LeadRouting.Choose(settings.List, lead.MarketingConsent);
        var outcome = await map.SubmitAsync(new MapSubmission(lead, list?.ListId, token.IdempotencyKey, now), cancellationToken).ConfigureAwait(false);
        var kind = outcome switch
        {
            MapOutcome.Accepted => LeadFormResultKind.ThankYou,
            MapOutcome.Unknown => LeadFormResultKind.OutcomeUnknown,
            MapOutcome.Rejected => LeadFormResultKind.NotAccepted,
            _ => LeadFormResultKind.TryAgain,
        };
        LogMapResult(logger, kind, outcome);
        return LeadFormResult.Of(kind);
    }

    private LeadFormResult Refuse(string reason)
    {
        LogResult(logger, LeadFormResultKind.Refused, reason);
        return LeadFormResult.Of(LeadFormResultKind.Refused);
    }

    // The result and a fixed reason word only: no field values, token, key or client address.
    [LoggerMessage(EventId = 4110, Level = LogLevel.Information, Message = "Lead form post: {Result} ({Reason})")]
    private static partial void LogResult(ILogger logger, LeadFormResultKind result, string reason);

    [LoggerMessage(EventId = 4111, Level = LogLevel.Information, Message = "Lead form post: {Result} (MAP: {Outcome})")]
    private static partial void LogMapResult(ILogger logger, LeadFormResultKind result, MapOutcome outcome);
}

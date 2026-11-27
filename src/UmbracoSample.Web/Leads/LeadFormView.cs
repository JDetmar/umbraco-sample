using Microsoft.Extensions.Options;
using UmbracoSample.Components;
using UmbracoSample.Leads;

namespace UmbracoSample.Web.Leads;

/// <summary>The fields a visitor posts. Only these bind; anything else in the post, such as a <c>listId</c>, is ignored (tampering).</summary>
public sealed class LeadFormFields
{
    public const int MaxTokenLength = 512;

    public string? RenderToken { get; set; }

    /// <summary>The honeypot: hidden from people, so any value means a bot.</summary>
    public string? Website { get; set; }

    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? Company { get; set; }

    public string? Message { get; set; }

    public bool MarketingConsent { get; set; }

    public string? ConsentVersion { get; set; }
}

/// <summary>
/// What the post-back leaves for the re-render of the same page. <see cref="BlockKey"/> is null when the posted token
/// did not read, so the result applies to every form on the page.
/// </summary>
public sealed record LeadFormState(Guid? BlockKey, LeadFormFields Fields, LeadFormResult Result);

/// <summary>Everything the Form view renders for one block.</summary>
public sealed record LeadFormViewModel(FormModel Form, string ConsentText, string RenderToken, LeadFormFields Values, LeadFormResult? Result);

/// <summary>
/// Prepares a Form block for rendering. A fresh view gets a new render token, and so a new idempotency key; the
/// re-render after a post keeps the posted token, so a resubmission reuses its key (threat model 0001, section 5).
/// Either way the page must not be stored by any cache.
/// </summary>
public sealed class LeadFormView(IHttpContextAccessor http, IOptions<LeadSiteOptions> options, IServiceProvider services)
{
    internal static readonly object StateKey = new();

    public const string NoStore = "private, no-store";

    /// <summary>Null when the form is switched off or its consent version has no wording: nothing is rendered.</summary>
    public LeadFormViewModel? For(FormModel form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var site = options.Value;
        if (!site.Enabled || !site.Consent.TryGetValue(form.ConsentVersion, out var consentText) || http.HttpContext is not { } context)
        {
            return null;
        }

        context.Response.Headers.CacheControl = NoStore;
        var tokens = services.GetRequiredService<RenderTokenProtector>();
        if (context.Items[StateKey] is not LeadFormState state || (state.BlockKey is { } key && key != form.BlockKey))
        {
            return new LeadFormViewModel(form, consentText, tokens.Issue(form.BlockKey), new LeadFormFields(), null);
        }

        // A refused post gets a new token (its token was bad, or a bot check failed); the MAP never saw its key. Every
        // other result keeps the posted token, so the next attempt reuses the key.
        var token = state.Result.Kind is LeadFormResultKind.Refused ? tokens.Issue(form.BlockKey) : state.Fields.RenderToken!;
        return new LeadFormViewModel(form, consentText, token, state.Fields, state.Result);
    }
}

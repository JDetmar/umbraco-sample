using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Website.Controllers;
using UmbracoSample.Domain.Leads;
using UmbracoSample.Leads;

namespace UmbracoSample.Web.Leads;

/// <summary>
/// The Form block's post-back (threat model 0001). Before this runs, the front-end body limit (16 KB, 413) and the
/// <c>lead-form</c> rate limit (429) have applied; the antiforgery filter then rejects a post without a valid token
/// (400). The page re-renders in place with the result, keeping the visitor's input, and is never cached.
/// </summary>
public sealed class LeadFormController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider,
    IOptions<LeadSiteOptions> options)
    : SurfaceController(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(LeadFormRateLimitPolicy.Name)]
    [RequestSizeLimit(LeadFormOptions.MaxBodyBytes)]
    [RequestFormLimits(ValueLengthLimit = LeadFormOptions.MaxBodyBytes, MultipartBodyLengthLimit = LeadFormOptions.MaxBodyBytes)]
    public async Task<IActionResult> Submit([FromForm] LeadFormFields fields, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Response.Headers.CacheControl = LeadFormView.NoStore;

        // Off, or posted to Umbraco's direct surface route rather than a page: there is no page to re-render.
        if (!options.Value.Enabled
            || !UmbracoContextAccessor.TryGetUmbracoContext(out var umbraco)
            || umbraco.PublishedRequest?.PublishedContent is null)
        {
            return NotFound();
        }

        var handler = HttpContext.RequestServices.GetRequiredService<LeadSubmissionHandler>();
        var tokens = HttpContext.RequestServices.GetRequiredService<RenderTokenProtector>();
        var post = new LeadFormPost(
            fields.RenderToken,
            fields.LeaveBlank,
            CaptchaResponse: null, // D1: no provider is chosen; the switch is off.
            new LeadInput(fields.Name, fields.Email, fields.Company, fields.Message, fields.MarketingConsent, fields.ConsentVersion));
        var result = await handler.HandleAsync(post, cancellationToken);

        // The view re-renders the visitor's input from the fields, not from model state.
        ModelState.Clear();
        // A refused post shows its notice on every form on the page: its token may name a block that is not here.
        var blockKey = result.Kind is not LeadFormResultKind.Refused && tokens.TryRead(fields.RenderToken, out var token) ? token.BlockKey : (Guid?)null;
        HttpContext.Items[LeadFormView.StateKey] = new LeadFormState(blockKey, fields, result);
        return CurrentUmbracoPage();
    }
}

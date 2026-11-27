using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Web;
using UmbracoSample.Components;
using UmbracoSample.Domain.Leads;
using UmbracoSample.Leads;

namespace UmbracoSample.Web.Leads;

/// <summary>
/// Finds a Form block on the page being posted to (threat model 0001, tampering). Only a published block on that page
/// counts, so a token rendered on one page does not submit on another, and an unpublished or removed block fails. The
/// list and the consent versions come from the block and this site's configuration, never from the request.
/// </summary>
public sealed class PublishedLeadFormCatalog(IUmbracoContextAccessor umbraco, IOptions<LeadSiteOptions> options) : ILeadFormCatalog
{
    public LeadFormSettings? Find(Guid blockKey)
    {
        if (!umbraco.TryGetUmbracoContext(out var context)
            || context.PublishedRequest?.PublishedContent?.GetProperty(PageComponents.BlocksAlias)?.GetValue() is not BlockListModel blocks)
        {
            return null;
        }

        var form = blocks
            .Where(block => block.Content.Key == blockKey)
            .Select(block => ComponentMapper.Map(new PublishedBlockSource(block.Content, NullLogger.Instance)))
            .OfType<FormModel>()
            .FirstOrDefault();

        return form is null ? null : Settings(form, options.Value);
    }

    /// <summary>Resolves the block's keys through configuration. An unknown list key means no list; an unknown version means none is accepted.</summary>
    public static LeadFormSettings Settings(FormModel form, LeadSiteOptions options)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(options);

        var list = options.Lists.TryGetValue(form.ListKey, out var configured) && configured is { ListId: { } id, Purpose: { } purpose }
            ? new PlatformList(form.ListKey, id, purpose)
            : null;
        IReadOnlySet<string> versions = options.Consent.ContainsKey(form.ConsentVersion)
            ? new HashSet<string>([form.ConsentVersion], StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        return new LeadFormSettings(list, versions);
    }
}

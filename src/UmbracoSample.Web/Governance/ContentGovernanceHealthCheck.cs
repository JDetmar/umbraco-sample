using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using UmbracoSample.Domain.Governance;

namespace UmbracoSample.Web.Governance;

/// <summary>
/// Flags published pages with no content owner, no review date or an overdue review (GOV-02, GOV-03), in the back
/// office under Settings, Health Check, "Content". Umbraco discovers it by its attribute and runs it on demand, or on a
/// schedule if health-check notifications are configured. It loads every published page through the content service and
/// reads the published values, not drafts. "Published" is the page's own state: a published page under an unpublished
/// parent is counted too. Dates are compared in UTC.
/// </summary>
[HealthCheck("4f1c2a7e-8d3b-4c55-9a61-2e7f0b9d3c18", "Content governance", Description = "Every published page has an owner and a review date that has not passed.", Group = "Content")]
public sealed class ContentGovernanceHealthCheck(IContentService contentService, TimeProvider clock) : HealthCheck
{
    /// <summary>The property aliases on the content governance composition.</summary>
    public const string OwnerAlias = "contentOwner";

    public const string ReviewDateAlias = "reviewDate";

    private const int PageSize = 500;

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var pages = PublishedPages()
            .Select(page => new ReviewedPage(page.Name ?? page.Key.ToString(), page.Id, PageReview.Assess(HasOwner(page), ReviewDate(page), today)))
            .ToList();

        return Task.FromResult(GovernanceReport.Summarise(pages));
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action) =>
        throw new InvalidOperationException("Content governance has no automatic fix: an editor sets the owner and review date.");

    private IEnumerable<IContent> PublishedPages()
    {
        foreach (var root in contentService.GetRootContent())
        {
            if (root.Published)
            {
                yield return root;
            }

            long page = 0;
            long total;
            do
            {
                var batch = contentService.GetPagedDescendants(root.Id, page++, PageSize, out total);
                foreach (var content in batch.Where(content => content.Published))
                {
                    yield return content;
                }
            }
            while (page * PageSize < total);
        }
    }

    private static bool HasOwner(IContent page) =>
        page.HasProperty(OwnerAlias) && page.GetValue(OwnerAlias, published: true) is { } owner && !string.IsNullOrWhiteSpace(owner.ToString());

    // Review date uses the Umbraco.DateTime editor, which the content service returns as DateTime; a policy test keeps the
    // uSync definition on that editor.
    private static DateOnly? ReviewDate(IContent page) =>
        page.HasProperty(ReviewDateAlias) && page.GetValue(ReviewDateAlias, published: true) is DateTime date ? DateOnly.FromDateTime(date) : null;
}

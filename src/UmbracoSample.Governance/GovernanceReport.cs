using System.Globalization;
using System.Net;
using Umbraco.Cms.Core.HealthChecks;
using UmbracoSample.Domain.Governance;

namespace UmbracoSample.Governance;

/// <summary>A published page and what is wrong with its ownership or review.</summary>
public sealed record ReviewedPage(string Name, int Id, ReviewFinding Findings);

/// <summary>Turns assessed pages into health check results: one per kind of finding, naming the pages.</summary>
public static class GovernanceReport
{
    /// <summary>At most this many pages are named per result; the count is always exact.</summary>
    public const int ListedPages = 20;

    private static readonly (ReviewFinding Finding, StatusResultType Result, string Message)[] Kinds =
    [
        (ReviewFinding.NoOwner, StatusResultType.Error, "have no content owner"),
        (ReviewFinding.NoReviewDate, StatusResultType.Error, "have no review date"),
        (ReviewFinding.Overdue, StatusResultType.Error, "are past their review date"),
        (ReviewFinding.DueSoon, StatusResultType.Warning, $"are due for review within {PageReview.DueSoonDays} days"),
    ];

    public static IEnumerable<HealthCheckStatus> Summarise(IReadOnlyCollection<ReviewedPage> pages)
    {
        var results = new List<HealthCheckStatus>();
        foreach (var (finding, result, message) in Kinds)
        {
            var flagged = pages.Where(page => page.Findings.HasFlag(finding)).OrderBy(page => page.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (flagged.Count == 0)
            {
                continue;
            }

            var names = string.Join(", ", flagged.Take(ListedPages).Select(page => $"{WebUtility.HtmlEncode(page.Name)} (id {page.Id.ToString(CultureInfo.InvariantCulture)})"));
            var more = flagged.Count > ListedPages ? $" and {flagged.Count - ListedPages} more" : string.Empty;
            results.Add(new HealthCheckStatus($"{flagged.Count} published page(s) {message}: {names}{more}.") { ResultType = result });
        }

        if (pages.Count == 0)
        {
            results.Add(new HealthCheckStatus("No published pages to check.") { ResultType = StatusResultType.Info });
        }
        else if (results.Count == 0)
        {
            results.Add(new HealthCheckStatus($"All {pages.Count} published page(s) have an owner and a review date that has not passed.") { ResultType = StatusResultType.Success });
        }

        return results;
    }
}

using Shouldly;
using Umbraco.Cms.Core.HealthChecks;
using UmbracoSample.Domain.Governance;
using UmbracoSample.Governance;

namespace UmbracoSample.Web.Tests.Governance;

public sealed class GovernanceReportTests
{
    [Fact]
    public void Pages_in_order_give_one_success()
    {
        var result = GovernanceReport.Summarise([new ReviewedPage("Home", 1, ReviewFinding.None)]).ShouldHaveSingleItem();

        result.ResultType.ShouldBe(StatusResultType.Success);
        result.Message.ShouldContain("All 1 published page(s)");
    }

    [Fact]
    public void Each_kind_of_finding_is_its_own_result_naming_its_pages()
    {
        var results = GovernanceReport.Summarise(
        [
            new ReviewedPage("About", 2, ReviewFinding.NoOwner | ReviewFinding.Overdue),
            new ReviewedPage("Contact", 3, ReviewFinding.DueSoon),
            new ReviewedPage("Home", 1, ReviewFinding.None),
        ]).ToList();

        results.Select(r => r.ResultType).ShouldBe([StatusResultType.Error, StatusResultType.Error, StatusResultType.Warning]);
        results[0].Message.ShouldBe("1 published page(s) have no content owner: About (id 2).");
        results[1].Message.ShouldContain("past their review date: About (id 2)");
        results[2].Message.ShouldContain("Contact (id 3)");
    }

    [Fact]
    public void An_empty_site_is_not_reported_as_all_in_order() =>
        GovernanceReport.Summarise([]).ShouldHaveSingleItem().ResultType.ShouldBe(StatusResultType.Info);

    [Fact]
    public void Long_lists_are_cut_but_the_count_stays_exact()
    {
        var pages = Enumerable.Range(1, GovernanceReport.ListedPages + 5).Select(i => new ReviewedPage($"Page {i:000}", i, ReviewFinding.NoReviewDate)).ToList();

        var result = GovernanceReport.Summarise(pages).ShouldHaveSingleItem();

        result.Message.ShouldStartWith($"{GovernanceReport.ListedPages + 5} published page(s) have no review date");
        result.Message.ShouldEndWith("and 5 more.");
    }

    [Fact]
    public void Page_names_are_encoded_because_the_back_office_shows_the_message() =>
        GovernanceReport.Summarise([new ReviewedPage("<img src=x onerror=alert(1)>", 9, ReviewFinding.NoOwner)])
            .ShouldHaveSingleItem().Message.ShouldNotContain("<img");
}

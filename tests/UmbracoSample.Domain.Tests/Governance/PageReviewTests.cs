using CsCheck;
using Shouldly;
using UmbracoSample.Domain.Governance;

namespace UmbracoSample.Domain.Tests.Governance;

public sealed class PageReviewTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);

    [Theory]
    [InlineData(true, 60, ReviewFinding.None)]
    [InlineData(true, 30, ReviewFinding.DueSoon)]
    [InlineData(true, 31, ReviewFinding.None)]
    [InlineData(true, 0, ReviewFinding.DueSoon)]
    [InlineData(true, -1, ReviewFinding.Overdue)]
    [InlineData(false, 60, ReviewFinding.NoOwner)]
    [InlineData(false, -10, ReviewFinding.NoOwner | ReviewFinding.Overdue)]
    [InlineData(false, 10, ReviewFinding.NoOwner | ReviewFinding.DueSoon)]
    public void A_page_is_assessed_against_today(bool hasOwner, int daysUntilReview, ReviewFinding expected) =>
        PageReview.Assess(hasOwner, Today.AddDays(daysUntilReview), Today).ShouldBe(expected);

    [Fact]
    public void A_page_without_a_review_date_is_flagged() =>
        PageReview.Assess(hasOwner: true, reviewDate: null, Today).ShouldBe(ReviewFinding.NoReviewDate);

    [Fact]
    public void A_page_with_neither_owner_nor_review_date_has_both_findings() =>
        PageReview.Assess(hasOwner: false, reviewDate: null, Today).ShouldBe(ReviewFinding.NoOwner | ReviewFinding.NoReviewDate);

    [Fact]
    public void Only_a_coming_review_needs_no_action()
    {
        PageReview.NeedsAction(ReviewFinding.None).ShouldBeFalse();
        PageReview.NeedsAction(ReviewFinding.DueSoon).ShouldBeFalse();
        PageReview.NeedsAction(ReviewFinding.NoOwner).ShouldBeTrue();
        PageReview.NeedsAction(ReviewFinding.NoReviewDate).ShouldBeTrue();
        PageReview.NeedsAction(ReviewFinding.Overdue).ShouldBeTrue();
    }

    [Fact]
    public void Overdue_and_due_soon_never_appear_together_and_a_page_never_moves_back_from_due_soon_or_overdue() =>
        Gen.Select(Gen.Bool, Gen.Int[-400, 400], Gen.Int[0, 400]).Sample((hasOwner, offset, later) =>
        {
            var review = Today.AddDays(offset);
            var now = PageReview.Assess(hasOwner, review, Today);
            var afterwards = PageReview.Assess(hasOwner, review, Today.AddDays(later));

            (now.HasFlag(ReviewFinding.Overdue) && now.HasFlag(ReviewFinding.DueSoon)).ShouldBeFalse();
            if (now.HasFlag(ReviewFinding.Overdue))
            {
                afterwards.HasFlag(ReviewFinding.Overdue).ShouldBeTrue();
            }

            if (now.HasFlag(ReviewFinding.DueSoon))
            {
                (afterwards.HasFlag(ReviewFinding.DueSoon) || afterwards.HasFlag(ReviewFinding.Overdue)).ShouldBeTrue();
            }

            afterwards.HasFlag(ReviewFinding.NoOwner).ShouldBe(!hasOwner);
        });
}

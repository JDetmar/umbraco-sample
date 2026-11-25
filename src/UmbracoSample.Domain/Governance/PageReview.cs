namespace UmbracoSample.Domain.Governance;

/// <summary>What is wrong with a page's ownership or review (GOV-02, GOV-03). A page can have several findings.</summary>
[Flags]
public enum ReviewFinding
{
    None = 0,

    /// <summary>No content owner is set.</summary>
    NoOwner = 1,

    /// <summary>No review date is set.</summary>
    NoReviewDate = 2,

    /// <summary>The review date has passed.</summary>
    Overdue = 4,

    /// <summary>The review date falls within the warning window.</summary>
    DueSoon = 8,
}

/// <summary>Assesses a page's owner and review date against today. Pure, so it is tested without the CMS.</summary>
public static class PageReview
{
    /// <summary>How far ahead a coming review is flagged.</summary>
    public const int DueSoonDays = 30;

    public static ReviewFinding Assess(bool hasOwner, DateOnly? reviewDate, DateOnly today)
    {
        var findings = hasOwner ? ReviewFinding.None : ReviewFinding.NoOwner;

        if (reviewDate is not { } due)
        {
            return findings | ReviewFinding.NoReviewDate;
        }

        if (due < today)
        {
            return findings | ReviewFinding.Overdue;
        }

        // Due today counts as due soon, not overdue: the review can still happen on the day.
        return due.DayNumber - today.DayNumber <= DueSoonDays ? findings | ReviewFinding.DueSoon : findings;
    }

    /// <summary>Findings that make a page non-compliant now, as opposed to a coming review.</summary>
    public static bool NeedsAction(ReviewFinding findings) =>
        (findings & (ReviewFinding.NoOwner | ReviewFinding.NoReviewDate | ReviewFinding.Overdue)) != ReviewFinding.None;
}

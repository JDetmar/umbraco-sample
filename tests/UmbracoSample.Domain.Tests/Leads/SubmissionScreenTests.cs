using CsCheck;
using Shouldly;
using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Domain.Tests.Leads;

/// <summary>Threat model 0001: honeypot and render-time window (3 s to 2 h).</summary>
public sealed class SubmissionScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(3, ScreenVerdict.Accept)]
    [InlineData(2.999, ScreenVerdict.TooFast)]
    [InlineData(0, ScreenVerdict.TooFast)]
    [InlineData(7200, ScreenVerdict.Accept)]
    [InlineData(7200.001, ScreenVerdict.Expired)]
    [InlineData(-30, ScreenVerdict.TooFast)]
    [InlineData(-30.001, ScreenVerdict.FromTheFuture)]
    public void The_render_time_must_be_inside_the_window(double secondsAgo, ScreenVerdict expected) =>
        SubmissionScreen.Check(null, Now.AddSeconds(-secondsAgo), Now).ShouldBe(expected);

    [Fact]
    public void A_filled_honeypot_is_refused_whatever_the_time() =>
        Gen.Select(Gen.String[1, 20], Gen.Double[-10_000, 10_000]).Sample((honeypot, secondsAgo) =>
            SubmissionScreen.Check(honeypot, Now.AddSeconds(-secondsAgo), Now).ShouldBe(ScreenVerdict.Honeypot));

    [Fact]
    public void Only_ages_inside_the_window_are_accepted() =>
        Gen.Double[-20_000, 20_000].Sample(secondsAgo =>
        {
            var accepted = SubmissionScreen.Check(string.Empty, Now.AddSeconds(-secondsAgo), Now) == ScreenVerdict.Accept;
            var age = TimeSpan.FromSeconds(secondsAgo);
            accepted.ShouldBe(age >= SubmissionScreen.MinimumAge && age <= SubmissionScreen.MaximumAge);
        });
}

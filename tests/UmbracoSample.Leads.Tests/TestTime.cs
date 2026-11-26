namespace UmbracoSample.Leads.Tests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class TestTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using UmbracoSample.Web.Health;
using uSync.Core;

namespace UmbracoSample.Web.Tests.Health;

public sealed class SchemaDriftEvaluationTests
{
    [Fact]
    public void Matching_schema_is_healthy()
    {
        var result = SchemaDriftHealthCheck.Evaluate([Report("ContentTypeHandler", ChangeType.NoChange, ChangeType.Hidden, ChangeType.Information)]);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData(ChangeType.Create)]
    [InlineData(ChangeType.Update)]
    [InlineData(ChangeType.Delete)]
    [InlineData(ChangeType.WillChange)]
    [InlineData(ChangeType.Removed)]
    [InlineData(ChangeType.Mismatch)]
    [InlineData(ChangeType.ParentMissing)]
    public void Any_drifted_item_is_unhealthy_and_named(ChangeType change)
    {
        var result = SchemaDriftHealthCheck.Evaluate(
        [
            Report("DataTypeHandler", ChangeType.NoChange),
            Report("ContentTypeHandler", ChangeType.NoChange, change),
        ]);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Data["items"].ShouldBeOfType<List<string>>().ShouldHaveSingleItem().ShouldBe("ContentTypeHandler:item1");
    }

    [Theory]
    [InlineData(ChangeType.Fail)]
    [InlineData(ChangeType.ImportFail)]
    public void A_failed_comparison_is_unhealthy(ChangeType change)
    {
        var result = SchemaDriftHealthCheck.Evaluate([Report("ContentTypeHandler", change)]);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldNotBeNull().ShouldContain("could not be compared");
    }

    [Fact]
    public void No_handlers_is_unhealthy_rather_than_vacuously_healthy()
    {
        var result = SchemaDriftHealthCheck.Evaluate([]);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public void Every_change_type_is_classified_deliberately()
    {
        // A new uSync ChangeType must be placed on purpose, not fall through as "no drift".
        ChangeType[] notDrift = [ChangeType.NoChange, ChangeType.Hidden, ChangeType.Information, ChangeType.Clean, ChangeType.Export, ChangeType.Import, ChangeType.Rolledback];
        var classified = SchemaDriftHealthCheck.DriftChanges
            .Concat(SchemaDriftHealthCheck.FailedChanges)
            .Concat(notDrift)
            .ToHashSet();

        Enum.GetValues<ChangeType>().Where(change => !classified.Contains(change)).ShouldBeEmpty();
    }

    private static HandlerReport Report(string handler, params ChangeType[] changes) =>
        new(handler, [.. changes.Select((change, index) => new ItemReport($"item{index}", change))]);
}

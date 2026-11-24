using System.Text.RegularExpressions;
using Shouldly;

namespace UmbracoSample.Repository.Tests;

/// <summary>
/// Architecture decision records (ES-019) are named ADR-NNNN-short-title.md and listed in the index, so they are easy to
/// find by name and none is orphaned.
/// </summary>
public sealed partial class AdrNamingPolicyTests
{
    private static readonly string[] NonRecords = ["README.md", "template.md"];

    [Fact]
    public void Every_record_is_named_ADR_NNNN_and_listed_in_the_index()
    {
        var folder = Path.Join(RepositoryRoot.Find().FullName, "docs", "adr");
        var index = File.ReadAllText(Path.Join(folder, "README.md"));
        var records = Directory.GetFiles(folder, "*.md")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !NonRecords.Contains(name))
            .ToList();

        records.ShouldNotBeEmpty();
        records.Where(name => !RecordName().IsMatch(name)).ShouldBeEmpty("ADRs are named ADR-NNNN-short-title.md.");
        records.Where(name => !index.Contains($"]({name})", StringComparison.Ordinal)).ShouldBeEmpty("Every ADR is linked from docs/adr/README.md.");
        records.Select(name => name[..8]).GroupBy(number => number).Where(g => g.Count() > 1).Select(g => g.Key).ShouldBeEmpty("ADR numbers are unique.");
    }

    [GeneratedRegex(@"^ADR-\d{4}-[a-z0-9]+(-[a-z0-9]+)*\.md$")]
    private static partial Regex RecordName();
}

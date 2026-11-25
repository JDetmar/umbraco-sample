using System.Text.RegularExpressions;
using Shouldly;

namespace UmbracoSample.Repository.Tests;

/// <summary>
/// docs/traceability.md lists every clause of the standard and every framework requirement once, and never claims more
/// than the conformance ledger (ES-038): a clause the ledger assesses shows the ledger's status and deviations, and a
/// clause it does not assess can only be "not assessed".
/// </summary>
public sealed partial class TraceabilityPolicyTests
{
    private static readonly string[] FrameworkStatuses = ["met", "partly", "gap", "planned", "deferred", "not applicable", "outside the code"];

    private static string Docs(string name) => File.ReadAllText(Path.Join(RepositoryRoot.Find().FullName, "docs", name));

    private static Dictionary<string, (string Status, string Evidence)> Rows(Regex row) =>
        row.Matches(Docs("traceability.md")).ToDictionary(m => m.Groups["id"].Value, m => (m.Groups["status"].Value.Trim(), m.Groups["evidence"].Value));

    [Fact]
    public void Every_clause_of_the_standard_is_listed_once()
    {
        var listed = StandardRow().Matches(Docs("traceability.md")).Select(m => m.Groups["id"].Value).ToList();

        listed.ShouldBe(Enumerable.Range(1, 61).Select(n => $"ES-{n:000}"));
    }

    [Fact]
    public void Every_framework_requirement_is_listed_once_with_a_known_status()
    {
        var required = FrameworkId().Matches(Docs("engineering-rigor-framework.md")).Select(m => m.Groups["id"].Value).Order().ToList();
        var listed = FrameworkRow().Matches(Docs("traceability.md")).Select(m => m.Groups["id"].Value).ToList();

        required.ShouldNotBeEmpty();
        listed.Order().ShouldBe(required);
        listed.Distinct().Count().ShouldBe(listed.Count);
        Rows(FrameworkRow()).Where(row => !FrameworkStatuses.Contains(row.Value.Status)).Select(row => $"{row.Key}: {row.Value.Status}").ShouldBeEmpty();
    }

    [Fact]
    public void Clause_statuses_and_deviations_match_the_ledger()
    {
        var ledger = Ledger();
        var rows = Rows(StandardRow());
        ledger.Values.ShouldContain(clause => clause.Deviations.Count > 0, "the ledger parser found the deviation links");

        foreach (var (clause, row) in rows)
        {
            if (ledger.TryGetValue(clause, out var assessed))
            {
                row.Status.ShouldBe(assessed.Status, $"{clause} must show the ledger's status.");
                assessed.Deviations.Where(id => !row.Evidence.Contains(id, StringComparison.Ordinal)).ShouldBeEmpty($"{clause} must name the ledger's deviations.");
            }
            else
            {
                row.Status.ShouldBe("not assessed", $"{clause} is not in the ledger, so the traceability page cannot claim a status for it.");
            }
        }
    }

    [Fact]
    public void Every_deviation_named_exists_in_the_ledger()
    {
        var known = DeviationId().Matches(Docs("engineering-conformance.yml")).Select(m => m.Groups["id"].Value).ToHashSet();

        DeviationReference().Matches(Docs("traceability.md")).Select(m => m.Value).Distinct().Where(id => !known.Contains(id)).ShouldBeEmpty();
    }

    private static Dictionary<string, (string Status, HashSet<string> Deviations)> Ledger()
    {
        var yaml = Docs("engineering-conformance.yml");
        var clauses = yaml[..yaml.IndexOf("\ndeviations:", StringComparison.Ordinal)];
        var blocks = LedgerClause().Matches(clauses).ToList();
        blocks.ShouldNotBeEmpty();

        var result = new Dictionary<string, (string, HashSet<string>)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var end = i + 1 < blocks.Count ? blocks[i + 1].Index : clauses.Length;
            var body = clauses[blocks[i].Index..end];
            result[blocks[i].Groups["id"].Value] = (blocks[i].Groups["status"].Value, LedgerDeviationLink().Matches(body).Select(m => m.Groups["id"].Value).ToHashSet());
        }

        return result;
    }

    [GeneratedRegex(@"^\| (?<id>ES-\d{3}) \| [^|]+ \| (?<status>[^|]+) \|(?<evidence>[^\n]*)$", RegexOptions.Multiline)]
    private static partial Regex StandardRow();

    [GeneratedRegex(@"^\| (?<id>(?!ES-)[A-Z]{2,4}-\d{2}) \| [^|]+ \| (?<status>[^|]+) \|(?<evidence>[^\n]*)$", RegexOptions.Multiline)]
    private static partial Regex FrameworkRow();

    [GeneratedRegex(@"^\| (?<id>[A-Z]{2,4}-\d{2}) \|", RegexOptions.Multiline)]
    private static partial Regex FrameworkId();

    [GeneratedRegex(@"^  (?<id>ES-\d{3}):\r?\n    status: (?<status>\w+)", RegexOptions.Multiline)]
    private static partial Regex LedgerClause();

    [GeneratedRegex(@"^  - id: (?<id>DEV-\d{3})", RegexOptions.Multiline)]
    private static partial Regex DeviationId();

    [GeneratedRegex(@"DEV-\d{3}")]
    private static partial Regex DeviationReference();

    // An obligation's formal link to a deviation, as opposed to a mention in evidence text.
    [GeneratedRegex(@"^\s+deviation: (?<id>DEV-\d{3})\s*$", RegexOptions.Multiline)]
    private static partial Regex LedgerDeviationLink();
}

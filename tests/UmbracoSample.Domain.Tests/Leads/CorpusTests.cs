using System.Text.Json;
using Shouldly;
using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Domain.Tests.Leads;

/// <summary>
/// Retained cases for the lead parser (ES-007). Every input that ever broke it, or that a property test shrank to, is
/// saved under corpus/ as valid-*.json (must parse) or invalid-*.json (must be rejected), in the LeadInput shape.
/// </summary>
public sealed class CorpusTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> Files =>
        [.. Directory.EnumerateFiles(Path.Join(AppContext.BaseDirectory, "Leads", "corpus"), "*.json").Select(file => Path.GetFileName(file)!).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Files))]
    public void Corpus_case_parses_as_expected(string file)
    {
        var input = JsonSerializer.Deserialize<LeadInput>(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Leads", "corpus", file)), Json)!;

        var result = LeadParser.Parse(input, TestRules.Default);

        result.IsValid.ShouldBe(file.StartsWith("valid-", StringComparison.Ordinal), string.Join("; ", result.Errors.Select(e => $"{e.Key} {e.Value}")));
        if (result.Lead is { } lead)
        {
            // A valid case must also come out clean: no control, format or separator characters survive.
            foreach (var text in new[] { lead.Name, lead.Company, lead.Message, lead.Email.Value })
            {
                text?.ShouldNotContain(c => c != '\n' && LeadParserTests.IsForbidden(c));
            }
        }
    }
}

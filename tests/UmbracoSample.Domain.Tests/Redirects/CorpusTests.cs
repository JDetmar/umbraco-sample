using Shouldly;
using UmbracoSample.Domain.Redirects;

namespace UmbracoSample.Domain.Tests.Redirects;

/// <summary>
/// Retained regression cases for the redirect parser (ES-007). Every input that ever broke the parser, or that a
/// property test shrank to, is saved under corpus/ as valid-*.csv (must parse) or invalid-*.csv (must be rejected).
/// </summary>
public sealed class CorpusTests
{
    private static readonly string[] Reserved = ["/umbraco", "/health"];

    public static TheoryData<string> Files =>
        [.. Directory.EnumerateFiles(Path.Join(AppContext.BaseDirectory, "Redirects", "corpus"), "*.csv").Select(file => Path.GetFileName(file)!).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Files))]
    public void Corpus_case_parses_as_expected(string file)
    {
        var text = File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Redirects", "corpus", file));

        var (map, errors) = RedirectMap.Parse(text, Reserved);

        if (file.StartsWith("valid-", StringComparison.Ordinal))
        {
            errors.ShouldBeEmpty();
            map.ShouldNotBeNull();
        }
        else
        {
            file.ShouldStartWith("invalid-");
            map.ShouldBeNull();
            errors.ShouldNotBeEmpty();
        }
    }
}

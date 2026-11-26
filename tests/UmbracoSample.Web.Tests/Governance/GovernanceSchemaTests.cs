using System.Xml.Linq;
using Shouldly;
using UmbracoSample.Web.Governance;

namespace UmbracoSample.Web.Tests.Governance;

/// <summary>
/// Keeps the uSync schema and the health check in step (ADR 0018): every page type carries the governance composition,
/// the aliases the check reads exist on it, and the review date stays on the editor the check reads as a DateTime.
/// </summary>
public sealed class GovernanceSchemaTests
{
    private const string CompositionAlias = "contentGovernance";

    private static readonly DirectoryInfo ContentTypes = new(Path.Join(RepositoryRoot(), "src", "UmbracoSample.Web", "uSync", "v17", "ContentTypes"));

    private static IEnumerable<XElement> AllContentTypes() =>
        ContentTypes.EnumerateFiles("*.config").Select(file => XDocument.Load(file.FullName).Root!);

    private static XElement Composition() =>
        AllContentTypes().Single(type => (string?)type.Attribute("Alias") == CompositionAlias);

    [Fact]
    public void Every_page_type_composes_content_governance()
    {
        var pageTypes = AllContentTypes()
            .Where(type => type.Element("Info")!.Element("IsElement")!.Value == "false")
            .Where(type => type.Element("Info")!.Element("AllowedTemplates")!.HasElements)
            .ToList();

        pageTypes.ShouldNotBeEmpty();
        pageTypes
            .Where(type => !type.Element("Info")!.Element("Compositions")!.Elements("Composition").Any(c => c.Value == CompositionAlias))
            .Select(type => (string?)type.Attribute("Alias"))
            .ShouldBeEmpty("A page type without the composition escapes the content governance check.");
    }

    [Theory]
    [InlineData(ContentGovernanceHealthCheck.OwnerAlias, "Umbraco.UserPicker")]
    [InlineData(ContentGovernanceHealthCheck.ReviewDateAlias, "Umbraco.DateTime")]
    public void The_check_reads_properties_that_exist_with_the_expected_editor(string alias, string editor)
    {
        var property = Composition().Descendants("GenericProperty").SingleOrDefault(p => p.Element("Alias")!.Value == alias);

        property.ShouldNotBeNull($"{alias} is on the {CompositionAlias} composition");
        property.Element("Type")!.Value.ShouldBe(editor);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "UmbracoSample.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

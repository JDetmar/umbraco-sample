using System.Text.Json;
using System.Xml.Linq;
using Shouldly;

namespace UmbracoSample.Web.Tests.Leads;

/// <summary>
/// Keeps the Form block's uSync schema as threat model 0001 describes it (section 1 and tampering): editors choose a list
/// key and a consent version from fixed dropdowns, never type a platform list ID or the consent wording, and every text
/// field has a length limit (boundary 4: settings are validated like input).
/// </summary>
public sealed class FormBlockSchemaTests
{
    private const string FormAlias = "formBlock";

    private static readonly string USync = Path.Join(RepositoryRoot(), "src", "UmbracoSample.Web", "uSync", "v17");

    private static XElement FormBlock() =>
        Directory.EnumerateFiles(Path.Join(USync, "ContentTypes"), "*.config")
            .Select(file => XDocument.Load(file).Root!)
            .Single(type => (string?)type.Attribute("Alias") == FormAlias);

    private static XElement DataType(string key) =>
        Directory.EnumerateFiles(Path.Join(USync, "DataTypes"), "*.config")
            .Select(file => XDocument.Load(file).Root!)
            .Single(type => (string?)type.Attribute("Key") == key);

    private static XElement Property(string alias) =>
        FormBlock().Descendants("GenericProperty").Single(p => p.Element("Alias")!.Value == alias);

    [Fact]
    public void The_form_block_is_an_element_type()
    {
        FormBlock().Element("Info")!.Element("IsElement")!.Value.ShouldBe("true");
    }

    [Theory]
    [InlineData("heading", "Umbraco.TextBox", true)]
    [InlineData("intro", "Umbraco.TextArea", false)]
    [InlineData("list", "Umbraco.DropDown.Flexible", true)]
    [InlineData("consentVersion", "Umbraco.DropDown.Flexible", true)]
    [InlineData("thankYou", "Umbraco.TextBox", true)]
    public void Each_field_has_the_planned_editor_and_is_required_as_planned(string alias, string editor, bool mandatory)
    {
        var property = Property(alias);

        property.Element("Type")!.Value.ShouldBe(editor);
        property.Element("Mandatory")!.Value.ShouldBe(mandatory ? "true" : "false");
    }

    [Fact]
    public void The_form_block_has_no_other_fields()
    {
        // The form's fields are fixed in code; a new block field needs the threat model and this test updated.
        FormBlock().Descendants("GenericProperty").Select(p => p.Element("Alias")!.Value).Order()
            .ShouldBe(["consentVersion", "heading", "intro", "list", "thankYou"]);
    }

    [Theory]
    [InlineData("heading", 120, 1)]
    [InlineData("intro", 500, 0)]
    [InlineData("thankYou", 200, 1)]
    public void Each_text_field_has_a_length_limit(string alias, int max, int min)
    {
        Property(alias).Element("Validation")!.Value.ShouldBe($"^[\\s\\S]{{{min},{max}}}$");
    }

    [Theory]
    [InlineData("list", new[] { "general-enquiry", "newsletter" })]
    [InlineData("consentVersion", new[] { "2026-11" })]
    public void The_dropdowns_offer_only_single_values_from_a_fixed_set(string alias, string[] items)
    {
        var config = Config(DataType(Property(alias).Element("Definition")!.Value));

        config.GetProperty("multiple").GetBoolean().ShouldBeFalse();
        config.GetProperty("items").EnumerateArray().Select(item => item.GetString()).ShouldBe(items);
    }

    private static JsonElement Config(XElement dataType) => JsonDocument.Parse(dataType.Element("Config")!.Value).RootElement;

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

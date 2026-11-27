using System.Text.Json;
using System.Xml.Linq;
using Shouldly;

namespace UmbracoSample.Web.Tests.Leads;

/// <summary>
/// Keeps the Form block's uSync schema as the step 4 plan and threat model 0001 describe it: editors choose a list key
/// and a consent version from fixed dropdowns, never type a platform list ID, and the block is offered on pages.
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
    [InlineData("consentText", "Umbraco.TextArea", true)]
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
            .ShouldBe(["consentText", "consentVersion", "heading", "intro", "list", "thankYou"]);
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

    [Fact]
    public void Pages_can_use_the_form_block()
    {
        var pageBlocks = Config(DataType("2e88d5e3-4ba5-488b-9918-9e4fdef493e0"));
        var formKey = (string)FormBlock().Attribute("Key")!;

        pageBlocks.GetProperty("blocks").EnumerateArray()
            .Select(block => block.GetProperty("contentElementTypeKey").GetString())
            .ShouldContain(formKey);
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

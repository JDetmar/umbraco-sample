using System.Text.Json;
using System.Xml.Linq;
using Shouldly;
using UmbracoSample.Components;
using UmbracoSample.Domain.Leads;
using UmbracoSample.Web.Leads;

namespace UmbracoSample.Web.Tests.Leads;

/// <summary>
/// The Form block's mapping and its resolution through configuration (threat model 0001, tampering and repudiation):
/// the list and consent wording come from the site's configuration by key, and the keys the back office offers are
/// exactly the configured ones.
/// </summary>
public sealed class FormBlockTests
{
    private static readonly Guid BlockKey = Guid.NewGuid();

    private static FakeBlockSourceBuilder Minimal() => new();

    [Fact]
    public void Minimal_form_has_its_required_fields_only()
    {
        var form = ComponentMapper.Map(Minimal().Build()).ShouldBeOfType<FormModel>();

        form.ShouldBe(new FormModel(BlockKey, "Contact us", Intro: null, "general-enquiry", "2026-11", "Thanks, we will be in touch."));
    }

    [Theory]
    [InlineData("heading")]
    [InlineData("list")]
    [InlineData("consentVersion")]
    [InlineData("thankYou")]
    public void A_form_missing_a_required_field_is_not_rendered(string alias) =>
        ComponentMapper.Map(Minimal().Without(alias).Build()).ShouldBeNull();

    [Fact]
    public void A_configured_list_and_version_resolve()
    {
        var settings = PublishedLeadFormCatalog.Settings(Form("newsletter", "2026-11"), Options());

        settings.List.ShouldBe(new PlatformList("newsletter", "list-news", ListPurpose.Marketing));
        settings.ConsentVersions.ShouldBe(["2026-11"]);
    }

    [Fact]
    public void An_unknown_list_key_means_no_list() =>
        PublishedLeadFormCatalog.Settings(Form("removed-list", "2026-11"), Options()).List.ShouldBeNull();

    [Fact]
    public void A_version_without_configured_wording_accepts_no_consent_version() =>
        PublishedLeadFormCatalog.Settings(Form("newsletter", "2099-01"), Options()).ConsentVersions.ShouldBeEmpty();

    [Fact]
    public void A_list_without_an_id_or_purpose_fails_validation()
    {
        var options = Options();
        options.Lists["broken"] = new LeadListOptions { ListId = "x" };

        options.Validate().ShouldNotBeNull();
        Options().Validate().ShouldBeNull();
    }

    [Fact]
    public void The_back_office_offers_exactly_the_configured_list_keys_and_consent_versions()
    {
        var leads = JsonDocument.Parse(File.ReadAllText(Path.Join(Root(), "src", "UmbracoSample.Web", "appsettings.json"))).RootElement.GetProperty("Leads");

        DropdownItems("Lead list").ShouldBe(leads.GetProperty("Lists").EnumerateObject().Select(p => p.Name), ignoreOrder: true);
        DropdownItems("Consent version").ShouldBe(leads.GetProperty("Consent").EnumerateObject().Select(p => p.Name), ignoreOrder: true);
        leads.GetProperty("Enabled").GetBoolean().ShouldBeFalse("the form stays off unless an environment turns it on (R2, R3)");
    }

    private static FormModel Form(string list, string version) => new(BlockKey, "Contact us", null, list, version, "Thanks");

    private static LeadSiteOptions Options() => new()
    {
        Lists =
        {
            ["general-enquiry"] = new LeadListOptions { ListId = "list-sales", Purpose = ListPurpose.Sales },
            ["newsletter"] = new LeadListOptions { ListId = "list-news", Purpose = ListPurpose.Marketing },
        },
        Consent = { ["2026-11"] = "Yes, email me." },
    };

    private static IEnumerable<string?> DropdownItems(string dataTypeName)
    {
        var dataType = Directory.EnumerateFiles(Path.Join(Root(), "src", "UmbracoSample.Web", "uSync", "v17", "DataTypes"), "*.config")
            .Select(file => XDocument.Load(file).Root!)
            .Single(type => type.Element("Info")!.Element("Name")!.Value == dataTypeName);
        return JsonDocument.Parse(dataType.Element("Config")!.Value).RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetString());
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "UmbracoSample.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FakeBlockSourceBuilder
    {
        private readonly Dictionary<string, string> _values = new()
        {
            ["heading"] = " Contact us ",
            ["list"] = "general-enquiry",
            ["consentVersion"] = "2026-11",
            ["thankYou"] = "Thanks, we will be in touch.",
        };

        public FakeBlockSourceBuilder Without(string alias)
        {
            _values.Remove(alias);
            return this;
        }

        public Components.FakeBlockSource Build()
        {
            var block = new Components.FakeBlockSource(ComponentMapper.FormAlias) { Key = BlockKey };
            foreach (var (alias, value) in _values)
            {
                block.With(alias, value);
            }

            return block;
        }
    }
}

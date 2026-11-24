using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;
using Shouldly;

namespace UmbracoSample.Infra.Tests;

/// <summary>Unit tests over the declared infrastructure with Pulumi mocks: no Azure account, no network (ES-001, ES-024).</summary>
public sealed class SiteInfrastructureTests
{
    private const string Digest = "ghcr.io/example/umbraco-sample@sha256:0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly SiteSettings Dev = new("dev", 0, 3, 20m, "owner@example.invalid", Digest, EnableEdge: false);

    private static async Task<RecordingMocks> Declare(SiteSettings settings)
    {
        var mocks = new RecordingMocks();
        await Deployment.TestAsync(mocks, new TestOptions { IsPreview = false, ProjectName = "umbraco-sample", StackName = settings.Environment },
            () => new SiteInfrastructure(settings).Outputs);
        return mocks;
    }

    private static ImmutableDictionary<string, object> Single(RecordingMocks mocks, string type) =>
        mocks.Resources.Where(r => r.Type == type).ShouldHaveSingleItem().Inputs;

    private static ImmutableDictionary<string, object> Map(object value) => value.ShouldBeAssignableTo<ImmutableDictionary<string, object>>()!;

    [Fact]
    public async Task Storage_refuses_shared_keys_anonymous_access_and_plain_http()
    {
        var storage = Single(await Declare(Dev), "azure-native:storage:StorageAccount");

        storage["allowSharedKeyAccess"].ShouldBe(false);
        storage["allowBlobPublicAccess"].ShouldBe(false);
        storage["enableHttpsTrafficOnly"].ShouldBe(true);
        storage["minimumTlsVersion"].ShouldBe("TLS1_2");
    }

    [Fact]
    public async Task Key_vault_uses_rbac_with_purge_protection()
    {
        var properties = Map(Single(await Declare(Dev), "azure-native:keyvault:Vault")["properties"]);

        properties["enableRbacAuthorization"].ShouldBe(true);
        properties["enablePurgeProtection"].ShouldBe(true);
        properties["tenantId"].ShouldBe(RecordingMocks.TenantId);
    }

    [Fact]
    public async Task Sql_accepts_only_entra_identities_with_the_site_identity_as_administrator()
    {
        var administrators = Map(Single(await Declare(Dev), "azure-native:sql:Server")["administrators"]);

        administrators["azureADOnlyAuthentication"].ShouldBe(true);
        administrators["principalType"].ShouldBe("Application");
    }

    [Fact]
    public async Task No_resource_input_carries_a_password_or_key()
    {
        var mocks = await Declare(Dev);

        foreach (var (type, name, inputs) in mocks.Resources)
        {
            inputs.Keys.ShouldNotContain(k => k.Contains("password", StringComparison.OrdinalIgnoreCase), $"{type} {name}");
            inputs.Keys.ShouldNotContain(k => k.Equals("administratorLogin", StringComparison.OrdinalIgnoreCase), $"{type} {name}");
        }
    }

    [Fact]
    public async Task The_site_identity_gets_exactly_three_data_plane_roles()
    {
        var mocks = await Declare(Dev);
        var roles = mocks.Resources.Where(r => r.Type == "azure-native:authorization:RoleAssignment").ToList();

        roles.Select(r => ((string)r.Inputs["roleDefinitionId"]).Split('/')[^1]).ShouldBe(
            [SiteInfrastructure.StorageBlobDataContributor, SiteInfrastructure.KeyVaultSecretsUser, SiteInfrastructure.MonitoringMetricsPublisher],
            ignoreOrder: true);
        roles.ShouldAllBe(r => ((string)r.Inputs["roleDefinitionId"]).StartsWith($"/subscriptions/{RecordingMocks.SubscriptionId}/", StringComparison.Ordinal));
        roles.ShouldAllBe(r => (string)r.Inputs["principalType"] == "ServicePrincipal");
        roles.Select(r => (string)r.Inputs["scope"]).ShouldBe(
            ["/mock/azure-native:storage:StorageAccount/umbsampledevmedia", "/mock/azure-native:keyvault:Vault/umbraco-sample-dev-kv", "/mock/azure-native:applicationinsights:Component/umbraco-sample-dev-ai"],
            ignoreOrder: true,
            "ES-024: each role is scoped to the one resource it is for, never the group or subscription.");
    }

    [Fact]
    public async Task The_site_runs_the_digest_pinned_image_over_https_with_health_probes()
    {
        var app = Single(await Declare(Dev), "azure-native:app:ContainerApp");
        var configuration = Map(app["configuration"]);
        var ingress = Map(configuration["ingress"]);
        var container = Map(((ImmutableArray<object>)Map(app["template"])["containers"]).ShouldHaveSingleItem());

        container["image"].ShouldBe(Digest);
        ingress["allowInsecure"].ShouldBe(false);
        configuration["activeRevisionsMode"].ShouldBe("Multiple");
        ((ImmutableArray<object>)container["probes"]).Select(p => (string)Map(p)["type"]).ShouldBe(["Liveness", "Readiness"]);
    }

    [Fact]
    public async Task The_database_connection_uses_managed_identity_not_a_password()
    {
        var container = Map(((ImmutableArray<object>)Map(Single(await Declare(Dev), "azure-native:app:ContainerApp")["template"])["containers"])[0]);
        var connection = ((ImmutableArray<object>)container["env"]).Select(Map).Single(e => (string)e["name"] == "ConnectionStrings__umbracoDbDSN");

        ((string)connection["value"]).ShouldContain("Authentication=Active Directory Managed Identity");
        ((string)connection["value"]).ShouldNotContain("Password", Case.Insensitive);
    }

    [Fact]
    public async Task Dev_uses_the_free_database_allowance_and_pauses_instead_of_billing()
    {
        var database = Single(await Declare(Dev), "azure-native:sql:Database");

        database["useFreeLimit"].ShouldBe(true);
        database["freeLimitExhaustionBehavior"].ShouldBe("AutoPause");
        database["autoPauseDelay"].ShouldBe(60);
    }

    [Fact]
    public async Task A_budget_alerts_before_the_money_runs_out()
    {
        var budget = Single(await Declare(Dev), "azure-native:consumption:Budget");

        budget["amount"].ShouldBe(20d);
        Map(budget["notifications"]).Keys.ShouldBe(["actual-80", "forecast-100"], ignoreOrder: true);
    }

    [Fact]
    public async Task Every_resource_that_takes_tags_is_tagged_with_its_environment()
    {
        var mocks = await Declare(Dev);

        foreach (var (type, name, inputs) in mocks.Resources.Where(r => r.Inputs.ContainsKey("tags")))
        {
            Map(inputs["tags"])["environment"].ShouldBe("dev", $"{type} {name}");
        }

        mocks.Resources.Count(r => r.Inputs.ContainsKey("tags")).ShouldBeGreaterThanOrEqualTo(8);
    }

    [Fact]
    public async Task A_stack_that_needs_the_edge_fails_until_the_edge_is_declared()
    {
        var prod = new SiteSettings("prod", 1, 3, 60m, "owner@example.invalid", Digest, EnableEdge: true);

        await Should.ThrowAsync<Exception>(() => Declare(prod));
    }
}

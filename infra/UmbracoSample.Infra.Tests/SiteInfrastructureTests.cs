using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;
using Shouldly;

namespace UmbracoSample.Infra.Tests;

/// <summary>Unit tests over the declared infrastructure with Pulumi mocks: no Azure account, no network (ES-001, ES-024).</summary>
public sealed class SiteInfrastructureTests
{
    private const string Digest = "ghcr.io/example/umbraco-sample@sha256:1111111111111111111111111111111111111111111111111111111111111111";

    private static readonly SiteSettings Dev = new("dev", 0, 1, 20m, Digest, EnableEdge: false);

    private static readonly string DevSuffix = SiteInfrastructure.Suffix(RecordingMocks.SubscriptionId, "dev");

    private const string AdminPassword = "mock-admin-password-value";
    private const string ImagingKey = "mock-imaging-hmac-key-value";

    private static SiteSecrets Secrets() => new(
        Output.CreateSecret(AdminPassword),
        Output.CreateSecret(ImagingKey),
        Output.CreateSecret("admin@example.invalid"),
        Output.CreateSecret("owner@example.invalid"),
        Output.CreateSecret("owners-sql"),
        Output.CreateSecret("00000000-0000-0000-0000-0000000000aa"));

    private static async Task<RecordingMocks> Declare(SiteSettings settings)
    {
        var mocks = new RecordingMocks();
        await Deployment.TestAsync(mocks, new TestOptions { IsPreview = false, ProjectName = "umbraco-sample", StackName = settings.Environment },
            () => new SiteInfrastructure(settings, Secrets()).Outputs);
        return mocks;
    }

    private static ImmutableDictionary<string, object> Single(RecordingMocks mocks, string type) =>
        mocks.Resources.Where(r => r.Type == type).ShouldHaveSingleItem().Inputs;

    private static ImmutableDictionary<string, object> Map(object value) => value.ShouldBeAssignableTo<ImmutableDictionary<string, object>>()!;

    private static ImmutableDictionary<string, object> SiteContainer(RecordingMocks mocks) =>
        Map(((ImmutableArray<object>)Map(Single(mocks, "azure-native:app:ContainerApp")["template"])["containers"]).ShouldHaveSingleItem());

    private static Dictionary<string, ImmutableDictionary<string, object>> Env(RecordingMocks mocks) =>
        ((ImmutableArray<object>)SiteContainer(mocks)["env"]).Select(Map).ToDictionary(e => (string)e["name"]);

    [Fact]
    public async Task Sql_accepts_connections_only_from_the_apps_subnet()
    {
        var mocks = await Declare(Dev);
        var subnet = Single(mocks, "azure-native:network:Subnet");
        var rule = Single(mocks, "azure-native:sql:VirtualNetworkRule");

        mocks.Resources.ShouldNotContain(r => r.Type == "azure-native:sql:FirewallRule", "ADR 0010: no IP rules and no 'allow Azure services'.");
        ((ImmutableArray<object>)subnet["serviceEndpoints"]).Select(e => (string)Map(e)["service"]).ShouldBe(["Microsoft.Sql"]);
        ((ImmutableArray<object>)subnet["delegations"]).Select(d => (string)Map(d)["serviceName"]).ShouldBe(["Microsoft.App/environments"]);
        rule["virtualNetworkSubnetId"].ShouldBe("/mock/azure-native:network:Subnet/apps");
        rule["ignoreMissingVnetServiceEndpoint"].ShouldBe(false);
    }

    [Fact]
    public async Task The_container_apps_environment_runs_in_the_apps_subnet()
    {
        var environment = Single(await Declare(Dev), "azure-native:app:ManagedEnvironment");
        var vnet = Map(environment["vnetConfiguration"]);

        vnet["infrastructureSubnetId"].ShouldBe("/mock/azure-native:network:Subnet/apps");
        vnet["internal"].ShouldBe(false);
        Map(Map(environment["peerTrafficConfiguration"])["encryption"])["enabled"].ShouldBe(true, "ES-025: no plain-HTTP hop inside the environment.");
    }

    [Fact]
    public async Task First_boot_installs_unattended_so_the_public_installer_is_never_reachable()
    {
        var env = Env(await Declare(Dev));

        env["Umbraco__CMS__Unattended__InstallUnattended"]["value"].ShouldBe("true");
        env["Umbraco__CMS__Unattended__UnattendedUserEmail"]["value"].ShouldBe("admin@example.invalid");
        env["Umbraco__CMS__Unattended__UnattendedUserPassword"]["secretRef"].ShouldBe("umbraco-admin-password");
        env["Umbraco__CMS__Imaging__HMACSecretKey"]["secretRef"].ShouldBe("umbraco-imaging-hmac-key");
        env["Umbraco__CMS__Unattended__UnattendedUserPassword"].ShouldNotContainKey("value");
        env["Umbraco__CMS__Imaging__HMACSecretKey"].ShouldNotContainKey("value");
    }

    [Fact]
    public async Task Media_goes_to_the_private_container_by_endpoint_never_by_key()
    {
        var env = Env(await Declare(Dev));

        env["Umbraco__Storage__AzureBlob__Media__ConnectionString"]["value"].ShouldBe($"https://{SiteInfrastructure.StorageAccountName("dev", DevSuffix)}.blob.core.windows.net/");
        env["Umbraco__Storage__AzureBlob__Media__ContainerName"]["value"].ShouldBe("media");
    }

    [Fact]
    public async Task Container_secrets_are_key_vault_references_read_with_the_site_identity()
    {
        var mocks = await Declare(Dev);
        var secrets = ((ImmutableArray<object>)Map(Single(mocks, "azure-native:app:ContainerApp")["configuration"])["secrets"]).Select(Map).ToList();

        secrets.Select(s => (string)s["name"]).ShouldBe(["umbraco-admin-password", "umbraco-imaging-hmac-key"], ignoreOrder: true);
        secrets.ShouldAllBe(s => !s.ContainsKey("value"));
        secrets.ShouldAllBe(s => ((string)s["keyVaultUrl"]).StartsWith("https://", StringComparison.Ordinal));
        secrets.ShouldAllBe(s => (string)s["identity"] == "/mock/azure-native:managedidentity:UserAssignedIdentity/umbraco-sample-dev-id");
        mocks.Resources.Count(r => r.Type == "azure-native:keyvault:Secret").ShouldBe(2);
    }

    [Fact]
    public async Task Secret_values_reach_only_key_vault()
    {
        var mocks = await Declare(Dev);

        foreach (var (type, name, inputs) in mocks.Resources.Where(r => r.Type != "azure-native:keyvault:Secret"))
        {
            var text = System.Text.Json.JsonSerializer.Serialize(inputs);
            text.ShouldNotContain(AdminPassword, customMessage: $"{type} {name}");
            text.ShouldNotContain(ImagingKey, customMessage: $"{type} {name}");
        }
    }

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
    public async Task Sql_accepts_only_entra_identities_and_an_owner_group_administers_it()
    {
        var server = Single(await Declare(Dev), "azure-native:sql:Server");
        var administrators = Map(server["administrators"]);

        administrators["azureADOnlyAuthentication"].ShouldBe(true);
        administrators["principalType"].ShouldBe("Group", "ES-023: the internet-facing site identity is never the server administrator.");
        administrators["sid"].ShouldBe("00000000-0000-0000-0000-0000000000aa");
        server["publicNetworkAccess"].ShouldBe("Enabled", "ADR 0010: virtual network rules work through the public endpoint.");
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("staging")]
    [InlineData("prod")]
    public void Global_names_fit_azure_rules_in_every_environment(string environment)
    {
        var suffix = SiteInfrastructure.Suffix("ffffffff-ffff-ffff-ffff-ffffffffffff", environment);
        var storage = SiteInfrastructure.StorageAccountName(environment, suffix);
        var vault = SiteInfrastructure.VaultName(environment, suffix);

        storage.ShouldMatch("^[a-z0-9]{3,24}$");
        vault.ShouldMatch("^[a-zA-Z](?!.*--)[a-zA-Z0-9-]{1,22}[a-zA-Z0-9]$");
        $"umbraco-sample-{environment}-web".Length.ShouldBeLessThanOrEqualTo(32, "Container App names are at most 32 characters.");
        SiteInfrastructure.Suffix("ffffffff-ffff-ffff-ffff-ffffffffffff", environment).ShouldBe(suffix, "names must be stable across runs");
    }

    [Fact]
    public async Task Resources_with_global_or_length_limited_names_are_named_explicitly()
    {
        var mocks = await Declare(Dev);

        Single(mocks, "azure-native:storage:StorageAccount")["accountName"].ShouldBe(SiteInfrastructure.StorageAccountName("dev", DevSuffix));
        Single(mocks, "azure-native:keyvault:Vault")["vaultName"].ShouldBe(SiteInfrastructure.VaultName("dev", DevSuffix));
        Single(mocks, "azure-native:app:ContainerApp")["containerAppName"].ShouldBe("umbraco-sample-dev-web");
        Single(mocks, "azure-native:storage:BlobContainer")["containerName"].ShouldBe("media");
        Single(mocks, "azure-native:sql:Database")["databaseName"].ShouldBe("umbraco");
    }

    [Fact]
    public async Task Logs_flow_through_diagnostic_settings_without_a_workspace_key()
    {
        var mocks = await Declare(Dev);
        var environment = Single(mocks, "azure-native:app:ManagedEnvironment");
        var diagnostics = Single(mocks, "azure-native:monitor:DiagnosticSetting");

        Map(environment["appLogsConfiguration"])["destination"].ShouldBe("azure-monitor");
        Map(environment["appLogsConfiguration"]).ShouldNotContainKey("logAnalyticsConfiguration");
        diagnostics["resourceUri"].ShouldBe("/mock/azure-native:app:ManagedEnvironment/umbraco-sample-dev-env");
        ((ImmutableArray<object>)diagnostics["logs"]).Select(l => (string)Map(l)["category"]).ShouldBe(["ContainerAppConsoleLogs", "ContainerAppSystemLogs"]);
    }

    [Fact]
    public async Task The_site_url_is_the_apps_stable_hostname_not_a_revisions()
    {
        SiteInfrastructure? infrastructure = null;
        await Deployment.TestAsync(new RecordingMocks(), new TestOptions { IsPreview = false, ProjectName = "umbraco-sample", StackName = "dev" },
            () =>
            {
                infrastructure = new SiteInfrastructure(Dev, Secrets());
                return infrastructure.Outputs;
            });

        var siteUrl = await Pulumi.Utilities.OutputUtilities.GetValueAsync((Output<string>)infrastructure!.Outputs["siteUrl"]!);
        siteUrl.ShouldBe("https://umbraco-sample-dev-web.app.example");
    }

    [Fact]
    public async Task No_resource_input_carries_a_password_or_key()
    {
        var mocks = await Declare(Dev);

        foreach (var (type, name, inputs) in mocks.Resources)
        {
            inputs.Keys.ShouldNotContain(k => k.Contains("password", StringComparison.OrdinalIgnoreCase), $"{type} {name}");
            inputs.Keys.ShouldNotContain(k => k.Equals("administratorLogin", StringComparison.OrdinalIgnoreCase), $"{type} {name}");
            System.Text.Json.JsonSerializer.Serialize(inputs).ShouldNotContain("\"sharedKey\":", Case.Insensitive, $"{type} {name}");
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
            ["/mock/azure-native:storage:StorageAccount/umbraco-sample-dev-media", "/mock/azure-native:keyvault:Vault/umbraco-sample-dev-kv", "/mock/azure-native:applicationinsights:Component/umbraco-sample-dev-ai"],
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
        ((ImmutableArray<object>)container["probes"]).Select(p => (string)Map(p)["type"]).ShouldBe(["Startup", "Liveness", "Readiness"]);
    }

    [Fact]
    public async Task The_database_connection_uses_managed_identity_not_a_password()
    {
        var connection = Env(await Declare(Dev))["ConnectionStrings__umbracoDbDSN"];

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
        // Azure accepts only the default max size on a free-limit, auto-pausing database.
        database.ContainsKey("maxSizeBytes").ShouldBeFalse();
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
        var prod = new SiteSettings("prod", 1, 1, 60m, Digest, EnableEdge: true);

        await Should.ThrowAsync<Exception>(() => Declare(prod));
    }
}

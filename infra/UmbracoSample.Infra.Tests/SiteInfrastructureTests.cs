using System.Collections.Immutable;
using System.Text.RegularExpressions;
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
        // Declared to match what Azure sets on the delegation; the dev check after deploy is what shows the drift is gone.
        var delegation = Map(((ImmutableArray<object>)subnet["delegations"]).ShouldHaveSingleItem());
        delegation.ShouldContainKey("actions");
        delegation["actions"].ShouldBeOfType<ImmutableArray<object>>().ShouldBe(["Microsoft.Network/virtualNetworks/subnets/join/action"]);
        delegation["type"].ShouldBe("Microsoft.Network/virtualNetworks/subnets/delegations");
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
    public async Task Up_creates_the_site_database_user_through_the_job_inside_the_network()
    {
        var command = (await Declare(Dev)).Resources.Single(r => r.Type == "command:local:Command" && r.Name == "site-database-user").Inputs;
        var environment = Map(command["environment"]);

        command["create"].ShouldBe("bash scripts/run-database-user-job.sh");
        command.ShouldNotContainKey("delete");
        environment["DATABASE"].ShouldBe("umbraco");
        environment["SITE_IDENTITY_NAME"].ShouldBe("umbraco-sample-dev-id");
        environment.Keys.ShouldBe(["JOB_ID", "SQL_SERVER_FQDN", "DATABASE", "SITE_IDENTITY_NAME", "SITE_IDENTITY_CLIENT_ID"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_database_user_job_runs_the_tool_from_the_site_image_with_no_identity_or_stored_token()
    {
        var job = Single(await Declare(Dev), "azure-native:app:Job");
        var container = Map(((System.Collections.IEnumerable)Map(job["template"])["containers"]).Cast<object>().Single());

        Map(job["configuration"])["triggerType"].ShouldBe("Manual");
        job.ShouldNotContainKey("identity");
        container["image"].ShouldBe(Dev.Image);
        container.ShouldNotContainKey("env");
    }

    [Fact]
    public async Task The_stack_exposes_what_the_restore_test_needs()
    {
        SiteInfrastructure? infrastructure = null;
        await Deployment.TestAsync(new RecordingMocks(), new TestOptions { IsPreview = false, ProjectName = "umbraco-sample", StackName = "dev" },
            () =>
            {
                infrastructure = new SiteInfrastructure(Dev, Secrets());
                return infrastructure.Outputs;
            });

        (await Pulumi.Utilities.OutputUtilities.GetValueAsync((Output<string>)infrastructure!.Outputs["database"]!)).ShouldBe("umbraco");
        (await Pulumi.Utilities.OutputUtilities.GetValueAsync((Output<string>)infrastructure.Outputs["databaseUserJob"]!)).ShouldBe("/mock/azure-native:app:Job/umbraco-sample-dev-db-user");
    }

    [Fact]
    public async Task The_app_waits_for_its_key_vault_access_to_propagate()
    {
        var wait = (await Declare(Dev)).Resources.Single(r => r.Type == "command:local:Command" && r.Name == "wait-for-secret-access").Inputs;

        wait["create"].ShouldBe("sleep 120");
    }

    [Fact]
    public void The_database_user_script_opens_no_firewall_rule_and_keeps_the_token_off_the_log()
    {
        var script = DatabaseUserScript();

        script.ShouldContain("set -euo pipefail");
        script.ShouldNotContain("firewall-rule");
        script.ShouldNotContain("ipify");
        script.ShouldContain("chmod 600");
        script.ShouldContain("trap 'rm -f \"$body\"' EXIT");
        // The token is fetched alone (so set -e stops on failure), checked, written only to the private body file,
        // and sent as --body "@file", never as a command-line argument or to the log.
        script.ShouldContain("token=\"$(az account get-access-token");
        script.ShouldContain("--body \"@${body}\"");
        Regex.Count(script, @"\$token\b|\$\{token\}").ShouldBe(2); // the format check and the printf argument
        script.ShouldNotContain("echo \"$token");
    }

    [Fact]
    public void The_script_starts_the_same_container_the_job_defines()
    {
        var script = DatabaseUserScript();

        script.ShouldContain($"container_name=\"{SiteInfrastructure.DatabaseUserContainer}\"");
        script.ShouldContain($"container_command='[\"dotnet\", \"{SiteInfrastructure.DatabaseUserEntryPoint}\"]'");
    }

    [Fact]
    public void The_site_image_carries_the_database_user_tool_where_the_job_runs_it()
    {
        var dockerfile = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../../src/UmbracoSample.Web/Dockerfile")));

        // The tool's package versions live in infra/Directory.Packages.props; without it the restore fails (NU1010).
        dockerfile.ShouldContain("COPY infra/Directory.Packages.props infra/");
        dockerfile.ShouldContain($"-o {Path.GetDirectoryName(SiteInfrastructure.DatabaseUserEntryPoint)!.Replace('\\', '/')} ");
    }

    private static string DatabaseUserScript() =>
        File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../UmbracoSample.Infra/scripts/run-database-user-job.sh")));

    [Fact]
    public async Task Umbraco_knows_its_public_url_so_production_mode_can_boot()
    {
        // The app's ingress host is <app name>.<environment default domain>; the mock environment's domain is env.region.example.
        Env(await Declare(Dev))["Umbraco__CMS__WebRouting__UmbracoApplicationUrl"]["value"].ShouldBe("https://umbraco-sample-dev-web.env.region.example/");
    }

    [Fact]
    public async Task Dev_imports_the_synthetic_seed_content_at_startup() =>
        Env(await Declare(Dev))["uSync__Settings__ImportAtStartup"]["value"].ShouldBe("All");

    [Fact]
    public async Task Other_environments_import_settings_only_at_startup() =>
        Env(await Declare(Dev with { Environment = "staging" }))["uSync__Settings__ImportAtStartup"]["value"].ShouldBe("Settings");

    [Theory]
    [InlineData("dev", "All")]
    [InlineData("staging", "Settings")]
    [InlineData("prod", "Settings")]
    public void Only_dev_imports_content(string environment, string expected) =>
        SiteInfrastructure.UsyncImportGroup(environment).ShouldBe(expected);

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
        $"site-sixteen-chr-{environment}-db-user".Length.ShouldBeLessThanOrEqualTo(32, "Container Apps job names are at most 32 characters, with the longest slug.");
        SiteInfrastructure.Suffix("ffffffff-ffff-ffff-ffff-ffffffffffff", environment).ShouldBe(suffix, "names must be stable across runs");
    }

    [Fact]
    public void The_default_site_keeps_the_names_it_had_before_site_slugs()
    {
        // ADR 0020: adding siteSlug must not rename, and so replace, any resource of an existing stack.
        // The first six hex characters of SHA-256("<subscription>/dev"), the formula before slugs, pinned as a literal.
        const string before = "f5b6c4";

        SiteInfrastructure.Suffix(RecordingMocks.SubscriptionId, "dev").ShouldBe(before);
        SiteInfrastructure.Suffix(RecordingMocks.SubscriptionId, "dev", SiteSettings.DefaultSiteSlug).ShouldBe(before);
    }

    [Fact]
    public async Task Another_site_gets_its_own_names_in_the_same_subscription()
    {
        var mocks = await Declare(Dev with { SiteSlug = "other-site" });
        var suffix = SiteInfrastructure.Suffix(RecordingMocks.SubscriptionId, "dev", "other-site");

        suffix.ShouldNotBe(DevSuffix);
        Single(mocks, "azure-native:storage:StorageAccount")["accountName"].ShouldBe(SiteInfrastructure.StorageAccountName("dev", suffix));
        Single(mocks, "azure-native:keyvault:Vault")["vaultName"].ShouldBe(SiteInfrastructure.VaultName("dev", suffix));
        Single(mocks, "azure-native:app:ContainerApp")["containerAppName"].ShouldBe("other-site-dev-web");
        Map(Single(mocks, "azure-native:resources:ResourceGroup")["tags"])["application"].ShouldBe("other-site");
    }

    [Fact]
    public async Task Resources_with_global_or_length_limited_names_are_named_explicitly()
    {
        var mocks = await Declare(Dev);

        Single(mocks, "azure-native:storage:StorageAccount")["accountName"].ShouldBe(SiteInfrastructure.StorageAccountName("dev", DevSuffix));
        Single(mocks, "azure-native:keyvault:Vault")["vaultName"].ShouldBe(SiteInfrastructure.VaultName("dev", DevSuffix));
        Single(mocks, "azure-native:app:ContainerApp")["containerAppName"].ShouldBe("umbraco-sample-dev-web");
        mocks.Resources.Where(r => r.Type == "azure-native:storage:BlobContainer").Select(r => (string)r.Inputs["containerName"])
            .ShouldBe(["media", SiteInfrastructure.KeyRingContainer], ignoreOrder: true);
        Single(mocks, "azure-native:keyvault:Key")["keyName"].ShouldBe("dataprotection");
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
        infrastructure.Outputs["siteImage"].ShouldBe(Dev.Image);
    }

    [Fact]
    public async Task Media_soft_delete_is_declared()
    {
        var recovery = Single(await Declare(Dev), "azure-native:storage:BlobServiceProperties");

        recovery["blobServicesName"].ShouldBe("default");
        recovery["accountName"].ShouldBe("umbraco-sample-dev-media");
        Map(recovery["deleteRetentionPolicy"])["enabled"].ShouldBe(true);
        Map(recovery["deleteRetentionPolicy"])["days"].ShouldBe(7);
        Map(recovery["deleteRetentionPolicy"])["allowPermanentDelete"].ShouldBe(false);
        Map(recovery["containerDeleteRetentionPolicy"])["enabled"].ShouldBe(true);
        Map(recovery["containerDeleteRetentionPolicy"])["days"].ShouldBe(7);
    }

    [Theory]
    [InlineData("dev", 7)]
    [InlineData("staging", 7)]
    [InlineData("prod", 30)]
    public void Prod_keeps_deleted_media_longer(string environment, int days) =>
        SiteInfrastructure.MediaRetentionDays(environment).ShouldBe(days);

    [Fact]
    public async Task Dev_declares_no_uptime_test_by_default()
    {
        var mocks = await Declare(Dev);

        mocks.Resources.ShouldNotContain(r => r.Type == "azure-native:applicationinsights:WebTest" || r.Type == "azure-native:monitor:MetricAlert" || r.Type == "azure-native:monitor:ActionGroup");
    }

    [Fact]
    public async Task The_uptime_test_probes_readiness_from_three_regions_and_alerts_the_contact()
    {
        var mocks = await Declare(Dev with { MinReplicas = 1, UptimeTest = true });
        var test = Single(mocks, "azure-native:applicationinsights:WebTest");
        var alert = Single(mocks, "azure-native:monitor:MetricAlert");
        var contacts = Single(mocks, "azure-native:monitor:ActionGroup");

        test["kind"].ShouldBe("standard");
        test["frequency"].ShouldBe(900);
        test["retryEnabled"].ShouldBe(true);
        Map(test["request"])["requestUrl"].ShouldBe("https://umbraco-sample-dev-web.app.example/health/ready");
        Map(test["validationRules"])["expectedHttpStatusCode"].ShouldBe(200);
        test["webTestKind"].ShouldBe("standard");
        ((ImmutableArray<object>)test["locations"]).Select(l => (string)Map(l)["location"]).ShouldBe(["emea-gb-db3-azr", "emea-nl-ams-azr", "us-va-ash-azr"]);
        Map(test["tags"])["hidden-link:/mock/azure-native:applicationinsights:Component/umbraco-sample-dev-ai"].ShouldBe("Resource");

        var criteria = Map(alert["criteria"]);
        criteria["odataType"].ShouldBe("Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria");
        criteria["failedLocationCount"].ShouldBe(2);
        criteria["webTestId"].ShouldBe("/mock/azure-native:applicationinsights:WebTest/umbraco-sample-dev-uptime");
        criteria["componentId"].ShouldBe("/mock/azure-native:applicationinsights:Component/umbraco-sample-dev-ai");
        // Azure requires exactly the test and its component as scopes, in this order.
        ((ImmutableArray<object>)alert["scopes"]).ShouldBe(["/mock/azure-native:applicationinsights:WebTest/umbraco-sample-dev-uptime", "/mock/azure-native:applicationinsights:Component/umbraco-sample-dev-ai"]);
        alert["enabled"].ShouldBe(true);

        var receiver = Map(((ImmutableArray<object>)contacts["emailReceivers"]).ShouldHaveSingleItem());
        receiver["emailAddress"].ShouldBe("owner@example.invalid");
        ((string)contacts["groupShortName"]).Length.ShouldBeLessThanOrEqualTo(12);
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
    public async Task Every_replica_shares_one_key_ring_in_a_private_blob_wrapped_by_a_key_vault_key()
    {
        var mocks = await Declare(Dev);
        var containers = mocks.Resources.Where(r => r.Type == "azure-native:storage:BlobContainer").ToDictionary(r => (string)r.Inputs["containerName"], r => r.Inputs);
        var key = Single(mocks, "azure-native:keyvault:Key");
        var env = Env(mocks);

        ((string)containers[SiteInfrastructure.KeyRingContainer]["publicAccess"]).ShouldBe("None");
        var properties = Map(key["properties"]);
        ((string)properties["kty"]).ShouldBe("RSA");
        Convert.ToInt32(properties["keySize"], System.Globalization.CultureInfo.InvariantCulture).ShouldBe(3072);
        ((string)env["DataProtection__KeyRingBlobUri"]["value"]).ShouldBe($"https://{SiteInfrastructure.StorageAccountName("dev", DevSuffix)}.blob.core.windows.net/dataprotection/keys.xml");
        ((string)env["DataProtection__KeyVaultKeyUri"]["value"]).ShouldBe("https://vault.example/keys/dataprotection");
    }

    [Fact]
    public async Task The_site_identity_gets_exactly_four_data_plane_roles()
    {
        var mocks = await Declare(Dev);
        var roles = mocks.Resources.Where(r => r.Type == "azure-native:authorization:RoleAssignment").ToList();

        roles.Select(r => ((string)r.Inputs["roleDefinitionId"]).Split('/')[^1]).ShouldBe(
            [SiteInfrastructure.StorageBlobDataContributor, SiteInfrastructure.KeyVaultSecretsUser, SiteInfrastructure.MonitoringMetricsPublisher, SiteInfrastructure.KeyVaultCryptoServiceEncryptionUser],
            ignoreOrder: true);
        roles.ShouldAllBe(r => ((string)r.Inputs["roleDefinitionId"]).StartsWith($"/subscriptions/{RecordingMocks.SubscriptionId}/", StringComparison.Ordinal));
        roles.ShouldAllBe(r => (string)r.Inputs["principalType"] == "ServicePrincipal");
        roles.Select(r => (string)r.Inputs["scope"]).ShouldBe(
            ["/mock/azure-native:storage:StorageAccount/umbraco-sample-dev-media", "/mock/azure-native:keyvault:Vault/umbraco-sample-dev-kv", "/mock/azure-native:applicationinsights:Component/umbraco-sample-dev-ai", "/mock/azure-native:keyvault:Key/dataprotection"],
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
        configuration["activeRevisionsMode"].ShouldBe("Single");
        ((ImmutableArray<object>)container["probes"]).Select(p => (string)Map(p)["type"]).ShouldBe(["Startup", "Liveness", "Readiness"]);
    }

    [Fact]
    public async Task The_database_connection_uses_managed_identity_not_a_password()
    {
        var connection = Env(await Declare(Dev))["ConnectionStrings__umbracoDbDSN"];

        ((string)connection["value"]).ShouldContain("Authentication=Active Directory Managed Identity");
        ((string)connection["value"]).ShouldNotContain("Password", Case.Insensitive);
        // Umbraco deprecates deriving its command timeout from the connect timeout, so set it explicitly.
        ((string)connection["value"]).ShouldContain("Command Timeout=60");
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

    [Theory]
    [InlineData(2026, 12, 15, "2026-12-01T00:00:00Z")]
    [InlineData(2027, 1, 1, "2027-01-01T00:00:00Z")]
    public void A_new_budget_starts_on_the_first_of_the_current_month(int year, int month, int day, string expected) =>
        SiteInfrastructure.BudgetStart(new DateTime(year, month, day, 23, 59, 0, DateTimeKind.Utc)).ShouldBe(expected);

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

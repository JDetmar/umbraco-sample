using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Pulumi;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;
using Pulumi.AzureNative.ApplicationInsights;
using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.Consumption;
using Pulumi.AzureNative.Consumption.Inputs;
using Pulumi.AzureNative.KeyVault;
using Pulumi.AzureNative.KeyVault.Inputs;
using Pulumi.AzureNative.ManagedIdentity;
using Pulumi.AzureNative.Monitor;
using Pulumi.AzureNative.Monitor.Inputs;
using Pulumi.AzureNative.Network;
using Pulumi.AzureNative.Network.Inputs;
using Pulumi.AzureNative.OperationalInsights;
using Pulumi.AzureNative.OperationalInsights.Inputs;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Sql;
using Pulumi.AzureNative.Sql.Inputs;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using AppSkuName = Pulumi.AzureNative.Sql.Inputs.SkuArgs;
using KeyVaultSecret = Pulumi.AzureNative.KeyVault.Secret;
using KeyVaultSecretArgs = Pulumi.AzureNative.KeyVault.SecretArgs;
using LocalCommand = Pulumi.Command.Local.Command;
using LocalCommandArgs = Pulumi.Command.Local.CommandArgs;
using ProbeType = Pulumi.AzureNative.App.Type;
using SqlPrincipalType = Pulumi.AzureNative.Sql.PrincipalType;
using SqlVirtualNetworkRule = Pulumi.AzureNative.Sql.VirtualNetworkRule;
using SqlVirtualNetworkRuleArgs = Pulumi.AzureNative.Sql.VirtualNetworkRuleArgs;
using StorageAccountArgs = Pulumi.AzureNative.Storage.StorageAccountArgs;
using StorageKind = Pulumi.AzureNative.Storage.Kind;
using StorageSkuArgs = Pulumi.AzureNative.Storage.Inputs.SkuArgs;
using StorageSkuName = Pulumi.AzureNative.Storage.SkuName;

namespace UmbracoSample.Infra;

/// <summary>
/// One environment of the site (ADR 0004, ADR 0010): Container Apps, Azure SQL serverless, Blob storage for media,
/// Key Vault, Log Analytics with Application Insights, one user-assigned identity, and a budget alert.
/// No resource uses a stored key or password: the site reaches SQL, storage and Key Vault with its managed identity.
/// </summary>
public sealed class SiteInfrastructure
{
    // Built-in role definition IDs (stable across tenants).
    public const string StorageBlobDataContributor = "ba92f5b4-2d11-453d-a403-e96b0029c9fe"; // gitleaks:allow public Azure built-in role ID
    public const string KeyVaultSecretsUser = "4633458b-17de-408a-b874-0445c86b69e6"; // gitleaks:allow public Azure built-in role ID
    public const string MonitoringMetricsPublisher = "3913510d-42f4-4e42-8a64-420c390055eb"; // gitleaks:allow public Azure built-in role ID

    public SiteInfrastructure(SiteSettings settings, SiteSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secrets);
        settings.Validate();

        // Fail closed: the edge (Front Door with WAF and HSTS) is not declared yet, so a stack that needs it cannot deploy without it (DEV-010).
        if (settings.EnableEdge)
        {
            throw new NotSupportedException("enableEdge is true but the edge is not declared yet; see ADR 0010 and DEV-010.");
        }

        var name = $"umbraco-sample-{settings.Environment}";
        var tags = new InputMap<string>
        {
            ["application"] = "umbraco-sample",
            ["environment"] = settings.Environment,
            ["managed-by"] = "pulumi",
        };

        var client = GetClientConfig.Invoke();
        var group = new ResourceGroup(name, new ResourceGroupArgs { Tags = tags });

        // Globally unique names are set explicitly: Pulumi's random auto-name suffix would push Key Vault and storage past
        // Azure's 24-character limits. A short hash of subscription and environment keeps them unique and stable.
        var suffix = client.Apply(c => Suffix(c.SubscriptionId, settings.Environment));

        var identity = new UserAssignedIdentity($"{name}-id", new UserAssignedIdentityArgs
        {
            ResourceGroupName = group.Name,
            ResourceName = $"{name}-id",
            Tags = tags,
        });

        // Telemetry: Log Analytics with a 30-day retention cap, and workspace-based Application Insights (OPS-01).
        var logs = new Workspace($"{name}-logs", new WorkspaceArgs
        {
            ResourceGroupName = group.Name,
            Sku = new WorkspaceSkuArgs { Name = WorkspaceSkuNameEnum.PerGB2018 },
            RetentionInDays = 30,
            // About 6 GB a month outside prod keeps logs inside the dev budget; prod gets 1 GB a day (ES-047).
            WorkspaceCapping = new WorkspaceCappingArgs { DailyQuotaGb = settings.Environment == "prod" ? 1 : 0.2 },
            Tags = tags,
        });

        var insights = new Component($"{name}-ai", new ComponentArgs
        {
            ResourceGroupName = group.Name,
            Kind = "web",
            ApplicationType = ApplicationType.Web,
            WorkspaceResourceId = logs.Id,
            DisableLocalAuth = true,
            Tags = tags,
        });

        // Media: Blob storage with no shared keys, no anonymous access and TLS 1.2 or later.
        var storage = new StorageAccount($"{name}-media", new StorageAccountArgs
        {
            ResourceGroupName = group.Name,
            AccountName = suffix.Apply(s => StorageAccountName(settings.Environment, s)),
            Kind = StorageKind.StorageV2,
            Sku = new StorageSkuArgs { Name = StorageSkuName.Standard_LRS },
            AllowBlobPublicAccess = false,
            AllowSharedKeyAccess = false,
            EnableHttpsTrafficOnly = true,
            MinimumTlsVersion = MinimumTlsVersion.TLS1_2,
            Tags = tags,
        });

        var media = new BlobContainer("media", new BlobContainerArgs
        {
            ResourceGroupName = group.Name,
            ContainerName = "media",
            AccountName = storage.Name,
            PublicAccess = PublicAccess.None,
        });

        // Secrets: RBAC-authorised Key Vault with purge protection.
        var vault = new Vault($"{name}-kv", new VaultArgs
        {
            ResourceGroupName = group.Name,
            VaultName = suffix.Apply(s => VaultName(settings.Environment, s)),
            Properties = new VaultPropertiesArgs
            {
                TenantId = client.Apply(c => c.TenantId),
                Sku = new Pulumi.AzureNative.KeyVault.Inputs.SkuArgs { Family = SkuFamily.A, Name = Pulumi.AzureNative.KeyVault.SkuName.Standard },
                EnableRbacAuthorization = true,
                EnablePurgeProtection = true,
                SoftDeleteRetentionInDays = 30,
            },
            Tags = tags,
        });

        // Database: Entra-only authentication (no SQL logins), serverless with auto-pause outside prod. An owner Entra group
        // administers the server; the site identity is only a contained db_owner user in the Umbraco database, created
        // once after the first deploy (docs/infra-setup.md), so a compromised site cannot administer the server (ES-023).
        var sqlServer = new Server($"{name}-sql", new ServerArgs
        {
            ResourceGroupName = group.Name,
            ServerName = suffix.Apply(s => $"{name}-sql-{s}"),
            MinimalTlsVersion = "1.2",
            // Virtual network rules work through the public endpoint; only the apps subnet is admitted (ADR 0010).
            PublicNetworkAccess = ServerPublicNetworkAccessFlag.Enabled,
            Administrators = new ServerExternalAdministratorArgs
            {
                AdministratorType = AdministratorType.ActiveDirectory,
                AzureADOnlyAuthentication = true,
                Login = secrets.SqlAdminGroupName,
                Sid = secrets.SqlAdminGroupObjectId,
                TenantId = client.Apply(c => c.TenantId),
                PrincipalType = SqlPrincipalType.Group,
            },
            Tags = tags,
        });

        // Network: one VNet whose only subnet hosts the Container Apps environment. The subnet has the Microsoft.Sql
        // service endpoint, and SQL accepts connections from that subnet only: no IP firewall rules, no "allow Azure
        // services" (ADR 0010). Service endpoints cost nothing; private endpoints are the upgrade if needed.
        var network = new VirtualNetwork($"{name}-vnet", new Pulumi.AzureNative.Network.VirtualNetworkArgs
        {
            ResourceGroupName = group.Name,
            AddressSpace = new AddressSpaceArgs { AddressPrefixes = { "10.40.0.0/16" } },
            Tags = tags,
        });

        var appsSubnet = new Pulumi.AzureNative.Network.Subnet("apps", new Pulumi.AzureNative.Network.SubnetArgs
        {
            ResourceGroupName = group.Name,
            VirtualNetworkName = network.Name,
            AddressPrefix = "10.40.0.0/23",
            Delegations = { new DelegationArgs { Name = "container-apps", ServiceName = "Microsoft.App/environments" } },
            ServiceEndpoints = { new ServiceEndpointPropertiesFormatArgs { Service = "Microsoft.Sql" } },
        });

        var sqlNetworkRule = new SqlVirtualNetworkRule("apps-subnet", new SqlVirtualNetworkRuleArgs
        {
            ResourceGroupName = group.Name,
            ServerName = sqlServer.Name,
            VirtualNetworkSubnetId = appsSubnet.Id,
            IgnoreMissingVnetServiceEndpoint = false,
        });

        var databaseArgs = new DatabaseArgs
        {
            ResourceGroupName = group.Name,
            DatabaseName = "umbraco",
            ServerName = sqlServer.Name,
            Sku = new AppSkuName { Name = "GP_S_Gen5", Tier = "GeneralPurpose", Family = "Gen5", Capacity = 1 },
            MinCapacity = 0.5,
            AutoPauseDelay = settings.Environment == "prod" ? -1 : 60,
            Tags = tags,
        };

        // Outside prod, use Azure SQL's free monthly allowance and pause when it runs out rather than bill (ES-047).
        if (settings.Environment != "prod")
        {
            databaseArgs.UseFreeLimit = true;
            databaseArgs.FreeLimitExhaustionBehavior = "AutoPause";
        }
        else
        {
            // Azure refuses a custom max size on a free-limit database that auto-pauses, so only prod caps it.
            databaseArgs.MaxSizeBytes = 5L * 1024 * 1024 * 1024;
        }

        var database = new Database("umbraco", databaseArgs);

        // The site identity gets exactly the data-plane roles it needs, scoped to the one resource each.
        _ = Grant("media-writer", storage.Id, StorageBlobDataContributor, identity, client);
        var secretsReader = Grant("secrets-reader", vault.Id, KeyVaultSecretsUser, identity, client);

        // A new role assignment can take a few minutes to reach Key Vault; without this wait the app's first revision
        // can fail to read its secrets. Runs only when the assignment is created or replaced.
        var secretAccessPropagated = new LocalCommand("wait-for-secret-access", new LocalCommandArgs
        {
            Create = "sleep 120",
            Triggers = secretsReader.Id.Apply(id => ImmutableArray.Create<object>(id)),
        }, new CustomResourceOptions { DependsOn = { secretsReader } });
        _ = Grant("telemetry-writer", insights.Id, MonitoringMetricsPublisher, identity, client);

        // Secrets the site needs at first boot. Set by the owner per stack; stored in Key Vault; referenced, never copied,
        // by the container app (ES-024). The admin password lets the first boot install Umbraco unattended, so the public
        // installer is never reachable; the HMAC key signs image-processing URLs (DEV-004).
        var adminPassword = StoreSecret("umbraco-admin-password", vault, group, secrets.AdminPassword);
        var imagingKey = StoreSecret("umbraco-imaging-hmac-key", vault, group, secrets.ImagingHmacKey);

        var environment = new ManagedEnvironment($"{name}-env", new ManagedEnvironmentArgs
        {
            ResourceGroupName = group.Name,
            VnetConfiguration = new VnetConfigurationArgs { InfrastructureSubnetId = appsSubnet.Id, Internal = false },
            // TLS on every hop inside the environment, including ingress proxy to site (ES-025; closes DEV-013).
            PeerTrafficConfiguration = new ManagedEnvironmentPeerTrafficConfigurationArgs
            {
                Encryption = new ManagedEnvironmentEncryptionArgs { Enabled = true },
            },
            WorkloadProfiles = { new WorkloadProfileArgs { Name = "Consumption", WorkloadProfileType = "Consumption" } },
            // Logs go through Azure Monitor diagnostic settings, so no workspace shared key is read or stored (ES-024).
            AppLogsConfiguration = new AppLogsConfigurationArgs { Destination = "azure-monitor" },
            Tags = tags,
        });

        _ = new DiagnosticSetting("environment-logs", new DiagnosticSettingArgs
        {
            ResourceUri = environment.Id,
            WorkspaceId = logs.Id,
            Logs =
            {
                new DiagnosticsLogSettingsArgs { Category = "ContainerAppConsoleLogs", Enabled = true },
                new DiagnosticsLogSettingsArgs { Category = "ContainerAppSystemLogs", Enabled = true },
            },
        });

        // The site's contained database user. Azure has no API for database users, and SQL admits only the apps subnet,
        // so the SQL runs from a manual Container Apps job in this environment, using the tool shipped in the site image
        // (ADR 0016). The job has no identity: `up` starts it with a short-lived SQL token of the deployer, a member of
        // the SQL administrator group, and waits for it. No firewall rule is opened. Preview never runs it.
        var databaseUserJob = new Pulumi.AzureNative.App.Job($"{name}-db-user", new Pulumi.AzureNative.App.JobArgs
        {
            ResourceGroupName = group.Name,
            JobName = $"{name}-db-user",
            EnvironmentId = environment.Id,
            WorkloadProfileName = "Consumption",
            Configuration = new JobConfigurationArgs
            {
                TriggerType = Pulumi.AzureNative.App.TriggerType.Manual,
                ReplicaTimeout = 600,
                ReplicaRetryLimit = 0,
                ManualTriggerConfig = new JobConfigurationManualTriggerConfigArgs { Parallelism = 1, ReplicaCompletionCount = 1 },
            },
            Template = new JobTemplateArgs
            {
                // The definition holds no token; each start passes the full container with its token (the script).
                Containers =
                {
                    new ContainerArgs
                    {
                        Name = DatabaseUserContainer,
                        Image = settings.Image,
                        Command = { "dotnet", DatabaseUserEntryPoint },
                        Resources = new ContainerResourcesArgs { Cpu = 0.25, Memory = "0.5Gi" },
                    },
                },
            },
            Tags = tags,
        });

        var databaseUser = new LocalCommand("site-database-user", new LocalCommandArgs
        {
            Create = "bash scripts/run-database-user-job.sh",
            Environment =
            {
                ["JOB_ID"] = databaseUserJob.Id,
                ["SQL_SERVER_FQDN"] = sqlServer.FullyQualifiedDomainName,
                ["DATABASE"] = database.Name,
                ["SITE_IDENTITY_NAME"] = identity.Name,
                ["SITE_IDENTITY_CLIENT_ID"] = identity.ClientId,
            },
            // Runs again only if the database, the identity or the job is replaced. The script reads the job's current
            // image itself, so a new image digest on every deploy does not re-run it.
            Triggers = Output.Tuple(database.Id, identity.ClientId, databaseUserJob.Id).Apply(t => ImmutableArray.Create<object>(t.Item1, t.Item2, t.Item3)),
        }, new CustomResourceOptions { DependsOn = { database, sqlNetworkRule, databaseUserJob } });

        var site = new ContainerApp($"{name}-web", new ContainerAppArgs
        {
            ResourceGroupName = group.Name,
            ContainerAppName = $"{name}-web",
            ManagedEnvironmentId = environment.Id,
            WorkloadProfileName = "Consumption",
            Identity = new Pulumi.AzureNative.App.Inputs.ManagedServiceIdentityArgs
            {
                Type = Pulumi.AzureNative.App.ManagedServiceIdentityType.UserAssigned,
                UserAssignedIdentities = { identity.Id },
            },
            Configuration = new ConfigurationArgs
            {
                // One active revision: each instance is the single Umbraco server and imports its uSync files at
                // startup, so an older revision left running could wake and roll the shared database back (ADR 0015).
                ActiveRevisionsMode = ActiveRevisionsMode.Single,
                Ingress = new IngressArgs
                {
                    External = true,
                    TargetPort = 8080,
                    AllowInsecure = false,
                    Transport = IngressTransportMethod.Auto,
                },
                Secrets =
                {
                    new Pulumi.AzureNative.App.Inputs.SecretArgs { Name = "umbraco-admin-password", KeyVaultUrl = adminPassword.Properties.Apply(p => p.SecretUri), Identity = identity.Id },
                    new Pulumi.AzureNative.App.Inputs.SecretArgs { Name = "umbraco-imaging-hmac-key", KeyVaultUrl = imagingKey.Properties.Apply(p => p.SecretUri), Identity = identity.Id },
                },
            },
            Template = new TemplateArgs
            {
                Containers =
                {
                    new ContainerArgs
                    {
                        Name = "web",
                        Image = settings.Image,
                        Resources = new ContainerResourcesArgs { Cpu = 1.0, Memory = "2Gi" },
                        Env =
                        {
                            new EnvironmentVarArgs { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production" },
                            new EnvironmentVarArgs { Name = "AZURE_CLIENT_ID", Value = identity.ClientId },
                            new EnvironmentVarArgs
                            {
                                Name = "ConnectionStrings__umbracoDbDSN",
                                Value = Output.Tuple(sqlServer.FullyQualifiedDomainName, database.Name, identity.ClientId).Apply(t =>
                                    $"Server=tcp:{t.Item1},1433;Database={t.Item2};Authentication=Active Directory Managed Identity;User Id={t.Item3};Encrypt=True;Connect Timeout=60;Command Timeout=60;Connect Retry Count=3;Connect Retry Interval=10"),
                            },
                            new EnvironmentVarArgs { Name = "ConnectionStrings__umbracoDbDSN_ProviderName", Value = "Microsoft.Data.SqlClient" },
                            new EnvironmentVarArgs { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", Value = insights.ConnectionString },
                            // Media in Blob storage via the managed identity: the endpoint only, never a key (MediaStorage in the site).
                            new EnvironmentVarArgs { Name = "Umbraco__Storage__AzureBlob__Media__ConnectionString", Value = storage.PrimaryEndpoints.Apply(e => e.Blob) },
                            new EnvironmentVarArgs { Name = "Umbraco__Storage__AzureBlob__Media__ContainerName", Value = media.Name },
                            // Umbraco's Production runtime mode refuses to boot without its public URL. With no edge, that is the
                            // app's own ingress host, built from the environment's domain (the app cannot reference its own output).
                            new EnvironmentVarArgs { Name = "Umbraco__CMS__WebRouting__UmbracoApplicationUrl", Value = environment.DefaultDomain.Apply(d => $"https://{name}-web.{d}/") },
                            new EnvironmentVarArgs { Name = "Umbraco__CMS__Unattended__InstallUnattended", Value = "true" },
                            new EnvironmentVarArgs { Name = "Umbraco__CMS__Unattended__UpgradeUnattended", Value = "true" },
                            new EnvironmentVarArgs { Name = "Umbraco__CMS__Unattended__UnattendedUserName", Value = "Site Admin" },
                            new EnvironmentVarArgs { Name = "Umbraco__CMS__Unattended__UnattendedUserEmail", Value = secrets.AdminEmail },
                            new EnvironmentVarArgs { Name = "Umbraco__CMS__Unattended__UnattendedUserPassword", SecretRef = "umbraco-admin-password" },
                            new EnvironmentVarArgs { Name = "Umbraco__CMS__Imaging__HMACSecretKey", SecretRef = "umbraco-imaging-hmac-key" },
                            // Dev imports the synthetic seed content shipped in the image (the home page; fixtures are
                            // left out of the publish), at every start so a database created before this setting still
                            // gets it. Every other environment imports settings only (CLAUDE.md, ES-027).
                            new EnvironmentVarArgs { Name = "uSync__Settings__ImportAtStartup", Value = UsyncImportGroup(settings.Environment) },
                        },
                        Probes =
                        {
                            // A cold start may wait for a paused serverless database to resume: allow five minutes to boot.
                            new ContainerAppProbeArgs { Type = ProbeType.Startup, HttpGet = new ContainerAppProbeHttpGetArgs { Path = "/health/live", Port = 8080 }, PeriodSeconds = 10, FailureThreshold = 30 },
                            new ContainerAppProbeArgs { Type = ProbeType.Liveness, HttpGet = new ContainerAppProbeHttpGetArgs { Path = "/health/live", Port = 8080 } },
                            new ContainerAppProbeArgs { Type = ProbeType.Readiness, HttpGet = new ContainerAppProbeHttpGetArgs { Path = "/health/ready", Port = 8080 } },
                        },
                    },
                },
                Scale = new ScaleArgs { MinReplicas = settings.MinReplicas, MaxReplicas = settings.MaxReplicas },
            },
            Tags = tags,
        }, new CustomResourceOptions
        {
            // The first revision boots straight into the unattended install, so the network rule, the database and the
            // Key Vault grant must exist first. RBAC can still take minutes to propagate; see docs/infra-setup.md.
            DependsOn = { secretsReader, secretAccessPropagated, sqlNetworkRule, database, databaseUser },
        });

        // ES-047: an alert before the money runs out, at 80% actual and 100% forecast spend.
        _ = new Budget($"{name}-budget", new BudgetArgs
        {
            Scope = group.Id,
            Amount = (double)settings.MonthlyBudget,
            Category = CategoryType.Cost,
            TimeGrain = TimeGrainType.Monthly,
            // Azure accepts a past start date only within the current month, so a new budget starts this month;
            // ignoreChanges keeps an existing budget from being rewritten every month.
            TimePeriod = new BudgetTimePeriodArgs { StartDate = BudgetStart(DateTime.UtcNow) },
            Notifications =
            {
                ["actual-80"] = new NotificationArgs { Enabled = true, Operator = OperatorType.GreaterThan, Threshold = 80, ContactEmails = { secrets.BudgetContactEmail } },
                ["forecast-100"] = new NotificationArgs { Enabled = true, Operator = OperatorType.GreaterThan, Threshold = 100, ThresholdType = ThresholdType.Forecasted, ContactEmails = { secrets.BudgetContactEmail } },
            },
        }, new CustomResourceOptions { IgnoreChanges = { "timePeriod" } });

        Outputs = new Dictionary<string, object?>
        {
            ["resourceGroup"] = group.Name,
            // The app's stable hostname, not one revision's: traffic splitting applies to it.
            ["siteUrl"] = site.Configuration.Apply(c => $"https://{c?.Ingress?.Fqdn}"),
            ["siteIdentity"] = identity.Name,
            ["sqlServer"] = sqlServer.FullyQualifiedDomainName,
            ["mediaContainer"] = media.Name,
            ["keyVault"] = vault.Name,
            ["edgeEnabled"] = settings.EnableEdge.ToString(CultureInfo.InvariantCulture),
        };
    }

    public IDictionary<string, object?> Outputs { get; }

    /// <summary>Six hex characters from subscription and environment: unique enough for global names, stable across runs.</summary>
    public static string Suffix(string subscriptionId, string environment) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{subscriptionId}/{environment}")))[..6];

    /// <summary>Storage account names are global: 3-24 lower-case letters and digits.</summary>
    public static string StorageAccountName(string environment, string suffix) => $"umbsample{environment}{suffix}";

    /// <summary>The database user job's container; the script that starts it sends the same name and command.</summary>
    public const string DatabaseUserContainer = "database-user";

    /// <summary>Where the Dockerfile publishes the database user tool inside the site image.</summary>
    public const string DatabaseUserEntryPoint = "/app/tools/database-user/UmbracoSample.Infra.DatabaseUser.dll";

    /// <summary>What uSync imports at startup: everything (schema and synthetic seed content) in dev only, settings elsewhere.</summary>
    public static string UsyncImportGroup(string environment) => environment == "dev" ? "All" : "Settings";

    /// <summary>The first day of the current month, UTC, in the form the budget API takes.</summary>
    public static string BudgetStart(DateTime utcNow) =>
        new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Key Vault names are global: 3-24 letters, digits and single hyphens, starting with a letter.</summary>
    public static string VaultName(string environment, string suffix) => $"umbs-{environment}-{suffix}";

    private static KeyVaultSecret StoreSecret(string secretName, Vault vault, ResourceGroup group, Output<string> value) =>
        new(secretName, new KeyVaultSecretArgs
        {
            ResourceGroupName = group.Name,
            VaultName = vault.Name,
            SecretName = secretName,
            Properties = new SecretPropertiesArgs { Value = value },
        });

    private static RoleAssignment Grant(string name, Output<string> scope, string roleId, UserAssignedIdentity identity, Output<GetClientConfigResult> client) =>
        new(name, new RoleAssignmentArgs
        {
            Scope = scope,
            PrincipalId = identity.PrincipalId,
            PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal,
            RoleDefinitionId = client.Apply(c => $"/subscriptions/{c.SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{roleId}"),
        });
}

using System.Collections.Immutable;
using System.Globalization;
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
using Pulumi.AzureNative.OperationalInsights;
using Pulumi.AzureNative.OperationalInsights.Inputs;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Sql;
using Pulumi.AzureNative.Sql.Inputs;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using AppSkuName = Pulumi.AzureNative.Sql.Inputs.SkuArgs;
using ProbeType = Pulumi.AzureNative.App.Type;
using SqlPrincipalType = Pulumi.AzureNative.Sql.PrincipalType;
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

    public SiteInfrastructure(SiteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
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

        var identity = new UserAssignedIdentity($"{name}-id", new UserAssignedIdentityArgs
        {
            ResourceGroupName = group.Name,
            Tags = tags,
        });

        // Telemetry: Log Analytics with a 30-day retention cap, and workspace-based Application Insights (OPS-01).
        var logs = new Workspace($"{name}-logs", new WorkspaceArgs
        {
            ResourceGroupName = group.Name,
            Sku = new WorkspaceSkuArgs { Name = WorkspaceSkuNameEnum.PerGB2018 },
            RetentionInDays = 30,
            WorkspaceCapping = new WorkspaceCappingArgs { DailyQuotaGb = 1 },
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
        var storage = new StorageAccount(StorageName(settings.Environment), new StorageAccountArgs
        {
            ResourceGroupName = group.Name,
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
            AccountName = storage.Name,
            PublicAccess = PublicAccess.None,
        });

        // Secrets: RBAC-authorised Key Vault with purge protection.
        var vault = new Vault($"{name}-kv", new VaultArgs
        {
            ResourceGroupName = group.Name,
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

        // Database: Entra-only authentication (no SQL logins), serverless with auto-pause outside prod.
        var sqlServer = new Server($"{name}-sql", new ServerArgs
        {
            ResourceGroupName = group.Name,
            MinimalTlsVersion = "1.2",
            Administrators = new ServerExternalAdministratorArgs
            {
                AdministratorType = AdministratorType.ActiveDirectory,
                AzureADOnlyAuthentication = true,
                Login = identity.Name,
                Sid = identity.PrincipalId,
                TenantId = client.Apply(c => c.TenantId),
                PrincipalType = SqlPrincipalType.Application,
            },
            Tags = tags,
        });

        // Only Azure services may connect; a private endpoint would cost more than the whole lean target (ADR 0010).
        _ = new FirewallRule("allow-azure-services", new FirewallRuleArgs
        {
            ResourceGroupName = group.Name,
            ServerName = sqlServer.Name,
            StartIpAddress = "0.0.0.0",
            EndIpAddress = "0.0.0.0",
        });

        var databaseArgs = new DatabaseArgs
        {
            ResourceGroupName = group.Name,
            ServerName = sqlServer.Name,
            Sku = new AppSkuName { Name = "GP_S_Gen5", Tier = "GeneralPurpose", Family = "Gen5", Capacity = 1 },
            MinCapacity = 0.5,
            AutoPauseDelay = settings.Environment == "prod" ? -1 : 60,
            MaxSizeBytes = 5L * 1024 * 1024 * 1024,
            Tags = tags,
        };

        // Outside prod, use Azure SQL's free monthly allowance and pause when it runs out rather than bill (ES-047).
        if (settings.Environment != "prod")
        {
            databaseArgs.UseFreeLimit = true;
            databaseArgs.FreeLimitExhaustionBehavior = "AutoPause";
        }

        var database = new Database("umbraco", databaseArgs);

        // The site identity gets exactly the data-plane roles it needs, scoped to the one resource each.
        _ = Grant("media-writer", storage.Id, StorageBlobDataContributor, identity, client);
        _ = Grant("secrets-reader", vault.Id, KeyVaultSecretsUser, identity, client);
        _ = Grant("telemetry-writer", insights.Id, MonitoringMetricsPublisher, identity, client);

        var environment = new ManagedEnvironment($"{name}-env", new ManagedEnvironmentArgs
        {
            ResourceGroupName = group.Name,
            AppLogsConfiguration = new AppLogsConfigurationArgs
            {
                Destination = "log-analytics",
                LogAnalyticsConfiguration = new LogAnalyticsConfigurationArgs
                {
                    CustomerId = logs.CustomerId,
                    SharedKey = GetSharedKeys.Invoke(new GetSharedKeysInvokeArgs { ResourceGroupName = group.Name, WorkspaceName = logs.Name })
                        .Apply(keys => keys.PrimarySharedKey!),
                },
            },
            Tags = tags,
        });

        var site = new ContainerApp($"{name}-web", new ContainerAppArgs
        {
            ResourceGroupName = group.Name,
            ManagedEnvironmentId = environment.Id,
            Identity = new Pulumi.AzureNative.App.Inputs.ManagedServiceIdentityArgs
            {
                Type = Pulumi.AzureNative.App.ManagedServiceIdentityType.UserAssigned,
                UserAssignedIdentities = { identity.Id },
            },
            Configuration = new ConfigurationArgs
            {
                // Multiple revisions: a failed compatible release is contained by shifting traffic back (ADR 0004, ES-014).
                ActiveRevisionsMode = ActiveRevisionsMode.Multiple,
                Ingress = new IngressArgs
                {
                    External = true,
                    TargetPort = 8080,
                    AllowInsecure = false,
                    Transport = IngressTransportMethod.Auto,
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
                                    $"Server=tcp:{t.Item1},1433;Database={t.Item2};Authentication=Active Directory Managed Identity;User Id={t.Item3};Encrypt=True"),
                            },
                            new EnvironmentVarArgs { Name = "ConnectionStrings__umbracoDbDSN_ProviderName", Value = "Microsoft.Data.SqlClient" },
                            new EnvironmentVarArgs { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", Value = insights.ConnectionString },
                        },
                        Probes =
                        {
                            new ContainerAppProbeArgs { Type = ProbeType.Liveness, HttpGet = new ContainerAppProbeHttpGetArgs { Path = "/health/live", Port = 8080 } },
                            new ContainerAppProbeArgs { Type = ProbeType.Readiness, HttpGet = new ContainerAppProbeHttpGetArgs { Path = "/health/ready", Port = 8080 } },
                        },
                    },
                },
                Scale = new ScaleArgs { MinReplicas = settings.MinReplicas, MaxReplicas = settings.MaxReplicas },
            },
            Tags = tags,
        });

        // ES-047: an alert before the money runs out, at 80% actual and 100% forecast spend.
        _ = new Budget($"{name}-budget", new BudgetArgs
        {
            Scope = group.Id,
            Amount = (double)settings.MonthlyBudget,
            Category = CategoryType.Cost,
            TimeGrain = TimeGrainType.Monthly,
            TimePeriod = new BudgetTimePeriodArgs { StartDate = "2026-12-01T00:00:00Z" },
            Notifications =
            {
                ["actual-80"] = new NotificationArgs { Enabled = true, Operator = OperatorType.GreaterThan, Threshold = 80, ContactEmails = { settings.BudgetContactEmail } },
                ["forecast-100"] = new NotificationArgs { Enabled = true, Operator = OperatorType.GreaterThan, Threshold = 100, ThresholdType = ThresholdType.Forecasted, ContactEmails = { settings.BudgetContactEmail } },
            },
        });

        Outputs = new Dictionary<string, object?>
        {
            ["resourceGroup"] = group.Name,
            ["siteUrl"] = site.LatestRevisionFqdn.Apply(fqdn => $"https://{fqdn}"),
            ["mediaContainer"] = media.Name,
            ["keyVault"] = vault.Name,
            ["edgeEnabled"] = settings.EnableEdge.ToString(CultureInfo.InvariantCulture),
        };
    }

    public IDictionary<string, object?> Outputs { get; }

    /// <summary>Storage account names are global, 3-24 lower-case letters and digits.</summary>
    public static string StorageName(string environment) => $"umbsample{environment}media";

    private static RoleAssignment Grant(string name, Output<string> scope, string roleId, UserAssignedIdentity identity, Output<GetClientConfigResult> client) =>
        new(name, new RoleAssignmentArgs
        {
            Scope = scope,
            PrincipalId = identity.PrincipalId,
            PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal,
            RoleDefinitionId = client.Apply(c => $"/subscriptions/{c.SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{roleId}"),
        });
}

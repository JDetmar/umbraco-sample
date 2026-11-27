var builder = DistributedApplication.CreateBuilder(args);

// The database lives only as long as this run: every start installs Umbraco and imports the uSync schema and
// synthetic seed content from source, so local runs and integration tests always begin from Git (ES-027, ES-053).
// Pinned by digest (ES-013); CI's container smoke test uses the same digest.
var sql = builder.AddSqlServer("sql")
    .WithImageTag("2022-latest")
    .WithImageSHA256("4525e3efac9f3e241cb003bb2ff2a82818b502f37e12049d788b1a9fa7e13b7f");
var db = sql.AddDatabase("umbracoDbDSN", "umbraco");

var adminPassword = builder.AddParameter("umbraco-admin-password", secret: true);

// Umbraco writes a generated image-URL signing key into appsettings.json on first install if none is configured.
// Supplying one per run keeps secrets out of committed configuration (ES-024); deployed environments read theirs
// from Key Vault.
var imagingKey = builder.AddParameter(
    "umbraco-imaging-hmac-key",
    new GenerateParameterDefault { MinLength = 64, Special = false },
    secret: true);

// A local stand-in for the marketing platform (docs/map-api.md), with a key that lives only for this run. The lead
// form is on locally and in the integration tests; deployed environments keep it off until the edge and the shared
// key ring exist (threat model 0001, R2 and R3).
var mapApiKey = builder.AddParameter(
    "map-api-key",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true);
var mapStub = builder.AddDotnetProject("map-stub", "../../tests/UmbracoSample.MapStub/UmbracoSample.MapStub.csproj")
    .WithHttpEndpoint()
    .WithEnvironment("MapStub__ApiKey", mapApiKey)
    .WithHttpHealthCheck("/stub/health");

builder.AddDotnetProject("web", "../UmbracoSample.Web/UmbracoSample.Web.csproj")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("ConnectionStrings__umbracoDbDSN_ProviderName", "Microsoft.Data.SqlClient")
    .WithEnvironment("Umbraco__CMS__Unattended__InstallUnattended", "true")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserName", "Local Admin")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserEmail", "admin@example.com")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserPassword", adminPassword)
    .WithEnvironment("Umbraco__CMS__Imaging__HMACSecretKey", imagingKey)
    .WaitFor(mapStub)
    .WithEnvironment("Leads__Enabled", "true")
    .WithEnvironment("Leads__Map__BaseAddress", mapStub.GetEndpoint("http"))
    .WithEnvironment("Leads__Map__ApiKey", mapApiKey)
    .WithHttpHealthCheck("/health/ready");

builder.Build().Run();

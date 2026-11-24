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

builder.AddDotnetProject("web", "../UmbracoSample.Web/UmbracoSample.Web.csproj")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("ConnectionStrings__umbracoDbDSN_ProviderName", "Microsoft.Data.SqlClient")
    .WithEnvironment("Umbraco__CMS__Unattended__InstallUnattended", "true")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserName", "Local Admin")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserEmail", "admin@example.com")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserPassword", adminPassword)
    .WithEnvironment("Umbraco__CMS__Imaging__HMACSecretKey", imagingKey)
    .WithHttpHealthCheck("/health/ready");

builder.Build().Run();

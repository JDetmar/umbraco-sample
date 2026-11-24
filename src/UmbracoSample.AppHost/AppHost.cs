var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent);
var db = sql.AddDatabase("umbracoDbDSN", "umbraco");

var adminPassword = builder.AddParameter("umbraco-admin-password", secret: true);

builder.AddProject<Projects.UmbracoSample_Web>("web")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("ConnectionStrings__umbracoDbDSN_ProviderName", "Microsoft.Data.SqlClient")
    .WithEnvironment("Umbraco__CMS__Unattended__InstallUnattended", "true")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserName", "Local Admin")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserEmail", "admin@example.com")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserPassword", adminPassword)
    .WithHttpHealthCheck("/health");

builder.Build().Run();

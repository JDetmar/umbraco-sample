using UmbracoSample.Infra.DatabaseUser;

// No argument: create the site's database user (ADR 0017). "restore-check": check a restored copy (ADR 0019).
return args is ["restore-check"]
    ? await RestoreCheck.RunAsync(Environment.GetEnvironmentVariable, Console.Out, Console.Error, TimeSpan.FromSeconds(15)).ConfigureAwait(false)
    : await DatabaseUserGrant.RunAsync(Environment.GetEnvironmentVariable, Console.Out, Console.Error, TimeSpan.FromSeconds(15)).ConfigureAwait(false);

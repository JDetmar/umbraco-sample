using UmbracoSample.Infra.DatabaseUser;

return await DatabaseUserGrant.RunAsync(Environment.GetEnvironmentVariable, Console.Out, Console.Error, TimeSpan.FromSeconds(15)).ConfigureAwait(false);

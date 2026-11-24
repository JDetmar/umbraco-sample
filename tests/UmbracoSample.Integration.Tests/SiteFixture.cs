using System.Security.Cryptography;

[assembly: AssemblyFixture(typeof(UmbracoSample.Integration.Tests.SiteFixture))]

namespace UmbracoSample.Integration.Tests;

/// <summary>
/// Starts the real Aspire AppHost once per test run: a fresh SQL Server container, an unattended Umbraco install
/// and the uSync first-boot import (ES-006: integration tests exercise the infrastructure they claim to verify).
/// </summary>
public sealed class SiteFixture : IAsyncLifetime
{
    // ES-043: every wait has a deadline. Container pull, install and import can take a few minutes on a cold runner.
    private static readonly TimeSpan StartupDeadline = TimeSpan.FromMinutes(8);

    private DistributedApplication? _app;

    public HttpClient Web { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(StartupDeadline);

        // A throwaway admin password for this run only; never logged or persisted.
        var password = $"It-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}!";
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.UmbracoSample_AppHost>(
            [$"Parameters:umbraco-admin-password={password}"],
            deadline.Token);

        _app = await builder.BuildAsync(deadline.Token);
        await _app.StartAsync(deadline.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("web", deadline.Token);

        Web = _app.CreateHttpClient("web", "http");
        Web.Timeout = TimeSpan.FromSeconds(60);
    }

    public async ValueTask DisposeAsync()
    {
        Web?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

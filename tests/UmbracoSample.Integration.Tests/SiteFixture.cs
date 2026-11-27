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

    /// <summary>
    /// The Front Door ID the site trusts in this run only, so each lead form test can name its own client IP through
    /// <c>X-Azure-ClientIP</c> and get its own rate-limit window. Deployed environments keep the ID unset (R2, DEV-010).
    /// </summary>
    public string FrontDoorId { get; } = Guid.NewGuid().ToString();

    private DistributedApplication? _app;

    public HttpClient Web { get; private set; } = null!;

    /// <summary>The local MAP stub: stored leads, the outage switch and the OTLP sink for the site's spans.</summary>
    public HttpClient MapStub { get; private set; } = null!;

    public DistributedApplication App => _app ?? throw new InvalidOperationException("The stack has not started.");

    public async ValueTask InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(StartupDeadline);

        // A throwaway admin password for this run only; never logged or persisted.
        var password = $"It-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}!";
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.UmbracoSample_AppHost>(
            [$"Parameters:umbraco-admin-password={password}"],
            deadline.Token);

        // The site's spans go to the MAP stub's OTLP sink, quickly, so a test can search them (threat model 0001,
        // information disclosure).
        var stub = builder.CreateResourceBuilder<Aspire.Hosting.Dotnet.DotnetProjectResource>("map-stub");
        builder.CreateResourceBuilder<Aspire.Hosting.Dotnet.DotnetProjectResource>("web")
            .WithEnvironment("Leads__Edge__FrontDoorId", FrontDoorId)
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", stub.GetEndpoint("http"))
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf")
            .WithEnvironment("OTEL_BSP_SCHEDULE_DELAY", "200");

        _app = await builder.BuildAsync(deadline.Token);
        await _app.StartAsync(deadline.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("web", deadline.Token);

        Web = _app.CreateHttpClient("web", "http");
        Web.Timeout = TimeSpan.FromSeconds(60);
        MapStub = _app.CreateHttpClient("map-stub", "http");
    }

    public async ValueTask DisposeAsync()
    {
        Web?.Dispose();
        MapStub?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

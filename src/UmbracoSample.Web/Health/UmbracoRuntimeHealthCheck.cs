using Microsoft.Extensions.Diagnostics.HealthChecks;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace UmbracoSample.Web.Health;

/// <summary>
/// Ready only when Umbraco is fully running: not installing, upgrading or failed to boot (ES-046).
/// An instance waiting for a deliberate upgrade stays out of rotation instead of serving the upgrade screen.
/// </summary>
public sealed class UmbracoRuntimeHealthCheck(IRuntimeState runtimeState) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(runtimeState.Level == RuntimeLevel.Run
            ? HealthCheckResult.Healthy("Umbraco is running.")
            : HealthCheckResult.Unhealthy($"Umbraco runtime level is {runtimeState.Level}."));
}

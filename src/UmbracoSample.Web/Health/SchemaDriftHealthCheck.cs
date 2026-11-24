using Microsoft.Extensions.Diagnostics.HealthChecks;
using uSync.BackOffice;
using uSync.BackOffice.Configuration;
using uSync.BackOffice.Models;
using uSync.BackOffice.SyncHandlers;
using uSync.BackOffice.SyncHandlers.Models;
using uSync.Core;

namespace UmbracoSample.Web.Health;

/// <summary>
/// Reports whether this environment's schema (content types, data types, templates and other settings)
/// differs from the uSync files shipped with the build (ENV-06, TST-06).
/// It runs uSync in report mode only, so it never changes the database.
/// </summary>
/// <remarks>
/// It is exposed on its own endpoint, not in readiness: drift is an alert for a person to act on,
/// not a reason to take the site out of service.
/// </remarks>
public sealed class SchemaDriftHealthCheck(
    ISyncHandlerFactory handlerFactory,
    ISyncService syncService,
    ISyncConfigService syncConfig) : IHealthCheck
{
    public const string Tag = "schema";
    private const string SettingsGroup = "Settings";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var options = new SyncHandlerOptions(handlerFactory.DefaultSet, HandlerActions.Report) { Group = SettingsGroup };
        var folders = syncConfig.GetFolders();
        var drifted = new List<string>();

        foreach (var handler in handlerFactory.GetValidHandlers(options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var actions = await syncService.ReportHandlerAsync(
                handler.Handler.Alias,
                new uSyncImportOptions { HandlerSet = handlerFactory.DefaultSet, Folders = folders });

            drifted.AddRange(actions
                .Where(action => action.Change > ChangeType.NoChange)
                .Select(action => $"{handler.Handler.Alias}:{action.Name}"));
        }

        return drifted.Count == 0
            ? HealthCheckResult.Healthy("Schema matches the uSync files in this build.")
            : HealthCheckResult.Unhealthy(
                $"{drifted.Count} schema item(s) differ from the uSync files in this build.",
                data: new Dictionary<string, object> { ["items"] = drifted });
    }
}

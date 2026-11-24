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

    /// <summary>uSync report outcomes that mean the database differs from the files.</summary>
    public static readonly IReadOnlySet<ChangeType> DriftChanges = new HashSet<ChangeType>
    {
        ChangeType.Create,
        ChangeType.Update,
        ChangeType.Delete,
        ChangeType.WillChange,
        ChangeType.Removed,
        ChangeType.Mismatch,
        ChangeType.ParentMissing,
    };

    /// <summary>uSync report outcomes that mean the comparison itself failed, so drift cannot be ruled out.</summary>
    public static readonly IReadOnlySet<ChangeType> FailedChanges = new HashSet<ChangeType>
    {
        ChangeType.Fail,
        ChangeType.ImportFail,
    };

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var options = new SyncHandlerOptions(handlerFactory.DefaultSet, HandlerActions.Report) { Group = SettingsGroup };
        var folders = syncConfig.GetFolders();
        var reports = new List<HandlerReport>();

        foreach (var handler in handlerFactory.GetValidHandlers(options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var actions = await syncService.ReportHandlerAsync(
                handler.Handler.Alias,
                new uSyncImportOptions { HandlerSet = handlerFactory.DefaultSet, Folders = folders });

            reports.Add(new HandlerReport(handler.Handler.Alias, [.. actions.Select(action => new ItemReport(action.Name, action.Change))]));
        }

        return Evaluate(reports);
    }

    /// <summary>Turns per-handler uSync reports into a health result. Pure, so every outcome is unit-tested.</summary>
    public static HealthCheckResult Evaluate(IReadOnlyCollection<HandlerReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        // No handlers means nothing was compared: report that instead of a vacuous "healthy".
        if (reports.Count == 0)
        {
            return HealthCheckResult.Unhealthy("No uSync settings handlers ran, so schema drift cannot be checked.");
        }

        var failed = Items(reports, FailedChanges);
        if (failed.Count > 0)
        {
            return HealthCheckResult.Unhealthy(
                $"{failed.Count} schema item(s) could not be compared with the uSync files in this build.",
                data: new Dictionary<string, object> { ["items"] = failed });
        }

        var drifted = Items(reports, DriftChanges);
        return drifted.Count == 0
            ? HealthCheckResult.Healthy($"Schema matches the uSync files in this build ({reports.Count} handlers checked).")
            : HealthCheckResult.Unhealthy(
                $"{drifted.Count} schema item(s) differ from the uSync files in this build.",
                data: new Dictionary<string, object> { ["items"] = drifted });
    }

    private static List<string> Items(IEnumerable<HandlerReport> reports, IReadOnlySet<ChangeType> changes) =>
        [.. reports.SelectMany(report => report.Items
            .Where(item => changes.Contains(item.Change))
            .Select(item => $"{report.Handler}:{item.Name}"))];
}

/// <summary>The outcome of one uSync handler's report.</summary>
public sealed record HandlerReport(string Handler, IReadOnlyList<ItemReport> Items);

/// <summary>One item in a uSync report.</summary>
public sealed record ItemReport(string? Name, ChangeType Change);

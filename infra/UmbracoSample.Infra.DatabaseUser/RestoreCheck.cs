using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace UmbracoSample.Infra.DatabaseUser;

/// <summary>
/// Checks a restored copy of the site database (ADR 0019): it has the Umbraco schema and content. Read-only, and it
/// refuses any database whose name does not end in <see cref="CopySuffix"/>, so it can never be pointed at the live one.
/// Runs in the same job as <see cref="DatabaseUserGrant"/>, inside the network, with a token the caller passes.
/// </summary>
public static partial class RestoreCheck
{
    public const string CopySuffix = "-restore-test";

    /// <summary>Counts only: content nodes, and the migration state Umbraco records once installed.</summary>
    public const string Sql = """
        SELECT
            (SELECT COUNT_BIG(*) FROM dbo.umbracoNode) AS nodes,
            (SELECT COUNT_BIG(*) FROM dbo.umbracoKeyValue WHERE [key] LIKE N'Umbraco.Core.Upgrader.State%') AS migrationStates;
        """;

    public sealed record Request(string ServerFqdn, string Database)
    {
        public string ConnectionString => new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{ServerFqdn},1433",
            InitialCatalog = Database,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            ConnectTimeout = 60,
            ApplicationIntent = ApplicationIntent.ReadOnly,
        }.ConnectionString;
    }

    public static Request Parse(Func<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(env);
        string Required(string key) => env(key) is { Length: > 0 } value ? value : throw new ArgumentException($"{key} is not set.");

        var request = new Request(Required("SQL_SERVER_FQDN"), Required("DATABASE"));
        if (!ServerPattern().IsMatch(request.ServerFqdn))
        {
            throw new ArgumentException("SQL_SERVER_FQDN must be an Azure SQL server host name.");
        }

        if (!CopyPattern().IsMatch(request.Database))
        {
            throw new ArgumentException($"DATABASE must be a restore copy named <database>{CopySuffix}.");
        }

        return request;
    }

    /// <summary>0 when the copy has content and an installed schema, 1 when it does not, 2 on bad input.</summary>
    public static async Task<int> RunAsync(Func<string, string?> env, TextWriter output, TextWriter error, TimeSpan retryDelay)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (env("SQL_ACCESS_TOKEN") is not { Length: > 0 } token)
        {
            await error.WriteLineAsync("SQL_ACCESS_TOKEN is not set.").ConfigureAwait(false);
            return 2;
        }

        Request request;
        try
        {
            request = Parse(env);
        }
        catch (ArgumentException e)
        {
            await error.WriteLineAsync(e.Message).ConfigureAwait(false);
            return 2;
        }

        // A new copy may still be coming online, or paused.
        for (var attempt = 1; attempt <= DatabaseUserGrant.Attempts; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(request.ConnectionString) { AccessToken = token };
                await connection.OpenAsync().ConfigureAwait(false);
                await using var command = new SqlCommand(Sql, connection);
                await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                await reader.ReadAsync().ConfigureAwait(false);
                var nodes = reader.GetInt64(0);
                var states = reader.GetInt64(1);
                await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{request.Database}: {nodes} content nodes, {states} migration state rows.")).ConfigureAwait(false);
                return Verdict(nodes, states);
            }
            catch (SqlException e) when (attempt < DatabaseUserGrant.Attempts && DatabaseUserGrant.TransientErrors.Contains(e.Number))
            {
                await error.WriteLineAsync($"Attempt {attempt} of {DatabaseUserGrant.Attempts} failed ({e.Number}: {e.Message}); retrying.").ConfigureAwait(false);
                await Task.Delay(retryDelay).ConfigureAwait(false);
            }
        }

        return 1;
    }

    /// <summary>An installed Umbraco database always has its root node and a recorded migration state.</summary>
    public static int Verdict(long nodes, long migrationStates) => nodes > 0 && migrationStates > 0 ? 0 : 1;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,115}-restore-test$")]
    private static partial Regex CopyPattern();

    [GeneratedRegex(@"^[a-z0-9-]{1,63}\.database\.windows\.net$")]
    private static partial Regex ServerPattern();
}

using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace UmbracoSample.Infra.DatabaseUser;

/// <summary>
/// Makes the site's managed identity a contained <c>db_owner</c> user of its database. Idempotent. Signs in as whoever
/// runs it (Entra, through the Azure CLI or workload identity), who must be in the SQL administrator group.
/// </summary>
public static partial class DatabaseUserGrant
{
    /// <summary>
    /// The user is bound to the identity's client ID (SID form), so SQL needs no directory lookup. Names reach SQL only
    /// as parameters and through QUOTENAME.
    /// </summary>
    public const string Sql = """
        -- One transaction: a rebind that drops the old user and then fails to create the new one leaves nothing changed.
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @sid varbinary(16) = CONVERT(varbinary(16), CONVERT(uniqueidentifier, @clientId));
        DECLARE @sidLiteral nvarchar(64) = CONVERT(nvarchar(64), @sid, 1);
        -- A replaced identity keeps its name but gets a new client ID: rebind the user rather than keep the stale one.
        -- EXEC with parentheses accepts only literals and variables, not function calls, so each statement is built first.
        DECLARE @statement nvarchar(max);
        -- Only an external (Entra) user is rebound; a role or SQL user of the same name fails loudly instead. DROP USER
        -- also fails if the old user owns a schema, which is left for a person to resolve.
        IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name AND type = 'E' AND sid <> @sid)
        BEGIN
            SET @statement = N'DROP USER ' + QUOTENAME(@name);
            EXEC sys.sp_executesql @statement;
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
        BEGIN
            SET @statement = N'CREATE USER ' + QUOTENAME(@name) + N' WITH SID = ' + @sidLiteral + N', TYPE = E';
            EXEC sys.sp_executesql @statement;
        END;
        SET @statement = N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@name);
        EXEC sys.sp_executesql @statement;
        COMMIT TRANSACTION;
        """;

    public const int Attempts = 12;

    /// <summary>
    /// Errors worth waiting out: a paused or resuming serverless database (40613, 40197, 40501, 49918-49920), a network
    /// rule not applied yet (40615), and network timeouts (-2, 53, 10053, 10054, 10060). Anything else, such as a login
    /// the server refuses, fails at once.
    /// </summary>
    public static readonly IReadOnlySet<int> TransientErrors = new HashSet<int> { 40613, 40197, 40501, 40615, 49918, 49919, 49920, -2, 53, 10053, 10054, 10060 };

    private static readonly string[] SqlScope = ["https://database.windows.net/.default"];

    public sealed record Request(string ServerFqdn, string Database, string IdentityName, string IdentityClientId)
    {
        public string ConnectionString => new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{ServerFqdn},1433",
            InitialCatalog = Database,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            ConnectTimeout = 60,
        }.ConnectionString;
    }

    /// <summary>Reads and checks the inputs against the shapes Azure produces.</summary>
    public static Request Parse(Func<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(env);
        string Required(string key) => env(key) is { Length: > 0 } value ? value : throw new ArgumentException($"{key} is not set.");

        var request = new Request(Required("SQL_SERVER_FQDN"), Required("DATABASE"), Required("SITE_IDENTITY_NAME"), Required("SITE_IDENTITY_CLIENT_ID"));
        if (!ServerPattern().IsMatch(request.ServerFqdn))
        {
            throw new ArgumentException("SQL_SERVER_FQDN must be an Azure SQL server host name.");
        }

        if (!NamePattern().IsMatch(request.Database))
        {
            throw new ArgumentException("DATABASE has an unexpected format.");
        }

        if (!NamePattern().IsMatch(request.IdentityName))
        {
            throw new ArgumentException("SITE_IDENTITY_NAME has an unexpected format.");
        }

        if (!Guid.TryParseExact(request.IdentityClientId, "D", out _))
        {
            throw new ArgumentException("SITE_IDENTITY_CLIENT_ID must be a GUID.");
        }

        return request;
    }

    /// <summary>
    /// Signs in only with the SQL access token the deploy passes in <c>SQL_ACCESS_TOKEN</c>: a token of the deployer, a
    /// member of the SQL administrator group, valid for about an hour (ADR 0017). The job has no identity of its own and
    /// no other credential is tried.
    /// </summary>
    public static async Task<int> RunAsync(Func<string, string?> env, TextWriter output, TextWriter error, TimeSpan retryDelay)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(error);
        if (env("SQL_ACCESS_TOKEN") is not { Length: > 0 } token)
        {
            await error.WriteLineAsync("SQL_ACCESS_TOKEN is not set.").ConfigureAwait(false);
            return 2;
        }

        return await RunAsync(env, output, error, retryDelay, new PassedToken(token)).ConfigureAwait(false);
    }

    public static async Task<int> RunAsync(Func<string, string?> env, TextWriter output, TextWriter error, TimeSpan retryDelay, Azure.Core.TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
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

        // A paused serverless database can take a minute or two to accept connections.
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(SqlScope), CancellationToken.None).ConfigureAwait(false);
                await using var connection = new SqlConnection(request.ConnectionString) { AccessToken = token.Token };
                await connection.OpenAsync().ConfigureAwait(false);
                await using var command = new SqlCommand(Sql, connection);
                command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = request.IdentityName });
                command.Parameters.Add(new SqlParameter("@clientId", System.Data.SqlDbType.NVarChar, 36) { Value = request.IdentityClientId });
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                await output.WriteLineAsync($"{request.IdentityName} is db_owner of {request.Database}.").ConfigureAwait(false);
                return 0;
            }
            catch (SqlException e) when (attempt < Attempts && TransientErrors.Contains(e.Number))
            {
                await error.WriteLineAsync($"Attempt {attempt} of {Attempts} failed ({e.Number}: {e.Message}); retrying.").ConfigureAwait(false);
                await Task.Delay(retryDelay).ConfigureAwait(false);
            }
        }

        return 1;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[a-z0-9-]{1,63}\.database\.windows\.net$")]
    private static partial Regex ServerPattern();

    /// <summary>A token obtained by the caller; the job runs for minutes, well inside its lifetime.</summary>
    private sealed class PassedToken(string token) : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(token, DateTimeOffset.UtcNow.AddMinutes(5));

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}

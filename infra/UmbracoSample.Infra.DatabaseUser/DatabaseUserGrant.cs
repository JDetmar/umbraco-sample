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
        DECLARE @sid nvarchar(64) = CONVERT(nvarchar(64), CONVERT(varbinary(16), CONVERT(uniqueidentifier, @clientId)), 1);
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
            EXEC (N'CREATE USER ' + QUOTENAME(@name) + N' WITH SID = ' + @sid + N', TYPE = E');
        EXEC (N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@name));
        """;

    public const int Attempts = 12;

    public sealed record Request(string ServerFqdn, string Database, string IdentityName, string IdentityClientId)
    {
        public string ConnectionString => new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{ServerFqdn},1433",
            InitialCatalog = Database,
            Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault,
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

    public static async Task<int> RunAsync(Func<string, string?> env, TextWriter output, TextWriter error, TimeSpan retryDelay)
    {
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

        // A new firewall rule, or a paused serverless database, can take a minute or two to accept connections.
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(request.ConnectionString);
                await connection.OpenAsync().ConfigureAwait(false);
                await using var command = new SqlCommand(Sql, connection);
                command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = request.IdentityName });
                command.Parameters.Add(new SqlParameter("@clientId", System.Data.SqlDbType.NVarChar, 36) { Value = request.IdentityClientId });
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                await output.WriteLineAsync($"{request.IdentityName} is db_owner of {request.Database}.").ConfigureAwait(false);
                return 0;
            }
            catch (SqlException e) when (attempt < Attempts)
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
}

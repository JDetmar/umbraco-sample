using Shouldly;
using UmbracoSample.Infra.DatabaseUser;

namespace UmbracoSample.Infra.Tests;

public class DatabaseUserGrantTests
{
    private static readonly Dictionary<string, string> Valid = new()
    {
        ["SQL_SERVER_FQDN"] = "umbraco-sample-dev-sql-abc123.database.windows.net",
        ["DATABASE"] = "umbraco",
        ["SITE_IDENTITY_NAME"] = "umbraco-sample-dev-id",
        ["SITE_IDENTITY_CLIENT_ID"] = "00000000-0000-0000-0000-0000000000aa",
    };

    private static Func<string, string?> With(string key, string? value) =>
        k => k == key ? value : Valid.GetValueOrDefault(k);

    [Fact]
    public void Valid_inputs_become_an_entra_connection_to_the_one_database()
    {
        var request = DatabaseUserGrant.Parse(Valid.GetValueOrDefault);

        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(request.ConnectionString).Authentication.ShouldBe(Microsoft.Data.SqlClient.SqlAuthenticationMethod.ActiveDirectoryDefault);
        request.ConnectionString.ShouldContain("Initial Catalog=umbraco");
        request.ConnectionString.ShouldNotContain("Password", Case.Insensitive);
    }

    [Theory]
    [InlineData("SITE_IDENTITY_NAME", "x]; DROP USER [dbo")]
    [InlineData("SITE_IDENTITY_NAME", "")]
    [InlineData("DATABASE", "umbraco;--")]
    [InlineData("SITE_IDENTITY_CLIENT_ID", "not-a-guid")]
    [InlineData("SQL_SERVER_FQDN", "attacker.example")]
    public void Unexpected_inputs_are_refused_before_any_connection(string key, string value) =>
        Should.Throw<ArgumentException>(() => DatabaseUserGrant.Parse(With(key, value))).Message.ShouldContain(key);

    [Fact]
    public async Task A_refused_input_exits_without_retrying()
    {
        var error = new StringWriter();

        var code = await DatabaseUserGrant.RunAsync(With("SITE_IDENTITY_CLIENT_ID", null), TextWriter.Null, error, TimeSpan.Zero);

        code.ShouldBe(2);
        error.ToString().ShouldContain("SITE_IDENTITY_CLIENT_ID");
    }

    [Fact]
    public void The_sql_is_idempotent_and_takes_names_only_as_parameters()
    {
        DatabaseUserGrant.Sql.ShouldContain("IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)");
        DatabaseUserGrant.Sql.ShouldContain("QUOTENAME(@name)");
        DatabaseUserGrant.Sql.ShouldContain("TYPE = E");
        DatabaseUserGrant.Sql.ShouldContain("ALTER ROLE db_owner");
    }
}

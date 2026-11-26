using System.Text.RegularExpressions;
using Shouldly;
using UmbracoSample.Infra.DatabaseUser;

namespace UmbracoSample.Infra.Tests;

public class RestoreCheckTests
{
    private static readonly Dictionary<string, string> Valid = new()
    {
        ["SQL_SERVER_FQDN"] = "umbraco-sample-dev-sql-abc123.database.windows.net",
        ["DATABASE"] = "umbraco-restore-test",
        ["SQL_ACCESS_TOKEN"] = "token",
    };

    private static Func<string, string?> With(string key, string? value) =>
        k => k == key ? value : Valid.GetValueOrDefault(k);

    private static string Script() =>
        File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../UmbracoSample.Infra/scripts/restore-test.sh")));

    [Theory]
    [InlineData("umbraco")]
    [InlineData("umbraco-restore-test-x")]
    [InlineData("restore-test")]
    [InlineData("x;--restore-test")]
    public async Task Only_a_restore_copy_is_ever_checked(string database)
    {
        using var error = new StringWriter();

        var code = await RestoreCheck.RunAsync(With("DATABASE", database), TextWriter.Null, error, TimeSpan.Zero);

        code.ShouldBe(2);
        error.ToString().ShouldContain("restore copy");
    }

    [Fact]
    public async Task Without_a_passed_token_the_check_exits_before_any_connection()
    {
        using var error = new StringWriter();

        (await RestoreCheck.RunAsync(With("SQL_ACCESS_TOKEN", null), TextWriter.Null, error, TimeSpan.Zero)).ShouldBe(2);
        error.ToString().ShouldContain("SQL_ACCESS_TOKEN");
    }

    [Fact]
    public void The_check_only_reads()
    {
        Regex.IsMatch(RestoreCheck.Sql, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|EXEC|TRUNCATE|GRANT)\b", RegexOptions.IgnoreCase).ShouldBeFalse();
    }

    [Fact]
    public void System_nodes_alone_do_not_pass()
    {
        // An empty install has system and data type nodes but no content items; only documents not in the bin count.
        RestoreCheck.Sql.ShouldContain($"nodeObjectType = '{RestoreCheck.DocumentObjectType}' AND trashed = 0");
        RestoreCheck.Sql.ShouldContain("[key] = N'Umbraco.Core.Upgrader.State+Umbraco.Core'");
        RestoreCheck.Sql.ShouldNotContain("LIKE");
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(5, 0, 1)]
    public void A_copy_passes_only_with_content_and_an_installed_schema(long nodes, long states, int expected) =>
        RestoreCheck.Verdict(nodes, states).ShouldBe(expected);

    [Fact]
    public void The_script_only_creates_reads_or_deletes_the_copy()
    {
        var script = Script();

        script.ShouldContain("set -euo pipefail");
        script.ShouldContain("copy=\"${DATABASE}-restore-test\"");
        // Every Azure call is one of these; anything new fails here and needs review.
        string[] allowed = ["az sql db delete -g \"$RESOURCE_GROUP\" -s \"$server\" -n \"$copy\" --yes", "az sql db restore -g \"$RESOURCE_GROUP\" -s \"$server\" -n \"$DATABASE\" --dest-name \"$copy\"", "az rest --method get --url \"${JOB_ID}?${api}\"", "az rest --method post --url \"${JOB_ID}/start?${api}\"", "az rest --method get --url \"${execution}?${api}\"", "az account get-access-token --resource https://database.windows.net/"];
        var calls = Regex.Matches(script, @"(?<![\w-])az [^\n]*").Select(m => m.Value).Where(line => !line.StartsWith("az has", StringComparison.Ordinal)).ToList();
        calls.ShouldNotBeEmpty();
        calls.ShouldAllBe(call => allowed.Any(call.StartsWith));
        script.ShouldContain("trap cleanup EXIT");
        script.ShouldContain("trap 'rm -f \"$body\"; cleanup' EXIT");
        script.ShouldNotContain("firewall-rule");
        // The job runs the read-only check, passing the copy's name, never the live database's.
        script.ShouldContain($"container_name=\"{SiteInfrastructure.DatabaseUserContainer}\"");
        script.ShouldContain($"container_command='[\"dotnet\", \"{SiteInfrastructure.DatabaseUserEntryPoint}\", \"restore-check\"]'");
        script.ShouldContain("\"$container_name\" \"$image\" \"$container_command\" \"$SQL_SERVER_FQDN\" \"$copy\" \"$token\"");
    }

    [Fact]
    public void The_script_keeps_the_token_off_the_log()
    {
        var script = Script();

        script.ShouldContain("chmod 600");
        script.ShouldContain("--body \"@${body}\"");
        Regex.Count(script, @"\$token\b|\$\{token\}").ShouldBe(2); // the format check and the printf argument
        script.ShouldNotContain("echo \"$token");
    }
}

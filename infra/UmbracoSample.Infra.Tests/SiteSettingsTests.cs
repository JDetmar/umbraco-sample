using Shouldly;

namespace UmbracoSample.Infra.Tests;

public sealed class SiteSettingsTests
{
    private const string Digest = "ghcr.io/example/umbraco-sample@sha256:1111111111111111111111111111111111111111111111111111111111111111";

    private static readonly string[] OwnerKeys = ["budgetContactEmail", "adminEmail", "sqlAdminGroupName", "sqlAdminGroupObjectId", "umbracoAdminPassword", "imagingHmacKey"];

    private static SiteSettings Valid(string environment = "dev") => new(environment, 0, 1, 20m, Digest, EnableEdge: false);

    [Fact]
    public void Valid_dev_settings_pass() => Should.NotThrow(() => Valid().Validate());

    [Theory]
    [InlineData("ghcr.io/example/umbraco-sample:latest")]
    [InlineData("ghcr.io/example/umbraco-sample:1.0.0")]
    [InlineData("umbraco-sample@sha256:00")]
    [InlineData("ghcr.io/example/umbraco-sample@sha256:ZZ00000000000000000000000000000000000000000000000000000000000000")]
    public void A_mutable_or_malformed_image_reference_is_rejected(string image) =>
        Should.Throw<ArgumentException>(() => (Valid() with { Image = image }).Validate()).Message.ShouldContain("digest");

    [Theory]
    [InlineData("test")]
    [InlineData("")]
    [InlineData("Prod")]
    public void Only_known_environments_are_accepted(string environment) =>
        Should.Throw<ArgumentException>(() => Valid(environment).Validate());

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(4, 3)]
    [InlineData(0, 0)]
    public void Replica_bounds_must_be_consistent(int min, int max) =>
        Should.Throw<ArgumentException>(() => (Valid() with { MinReplicas = min, MaxReplicas = max }).Validate());

    [Fact]
    public void Prod_keeps_a_warm_replica() =>
        Should.Throw<ArgumentException>(() => (Valid("prod") with { MinReplicas = 0, EnableEdge = true }).Validate()).Message.ShouldContain("warm");

    [Fact]
    public void Prod_must_sit_behind_the_edge() =>
        Should.Throw<ArgumentException>(() => (Valid("prod") with { MinReplicas = 1 }).Validate()).Message.ShouldContain("edge");

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_budget_must_be_positive(int amount) =>
        Should.Throw<ArgumentException>(() => (Valid() with { MonthlyBudget = amount }).Validate());


    [Fact]
    public void Scaling_out_is_refused_until_umbraco_can_share_state() =>
        Should.Throw<ArgumentException>(() => (Valid() with { MaxReplicas = 2 }).Validate()).Message.ShouldContain("DEV-015");

    [Fact]
    public void The_placeholder_digest_is_refused() =>
        Should.Throw<ArgumentException>(() => (Valid() with { Image = "ghcr.io/example/umbraco-sample@sha256:0000000000000000000000000000000000000000000000000000000000000000" }).Validate()).Message.ShouldContain("placeholder");


    [Theory]
    [InlineData("")]
    [InlineData("admin")]
    [InlineData("Admin <admin@example.invalid>")]
    public void Owner_emails_must_be_plain_addresses(string email) =>
        Should.Throw<ArgumentException>(() => SiteSecrets.Email("adminEmail", email)).Message.ShouldContain("adminEmail");

    [Fact]
    public void The_sql_admin_group_needs_a_name_and_an_object_id()
    {
        Should.Throw<ArgumentException>(() => SiteSecrets.NotBlank("sqlAdminGroupName", " "));
        Should.Throw<ArgumentException>(() => SiteSecrets.ObjectId("sqlAdminGroupObjectId", "not-a-guid"));
        SiteSecrets.ObjectId("sqlAdminGroupObjectId", "00000000-0000-0000-0000-0000000000aa").ShouldNotBeEmpty();
    }

    [Fact]
    public void Committed_stack_files_hold_no_owner_identifying_plain_values()
    {
        var folder = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../UmbracoSample.Infra"));
        foreach (var file in Directory.GetFiles(folder, "Pulumi.*.yaml"))
        {
            var lines = File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith('#')).ToArray();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                System.Text.RegularExpressions.Regex.IsMatch(line, @"[\w.+-]+@[\w-]+\.[\w.]+").ShouldBeFalse($"{Path.GetFileName(file)}: emails are set with --secret (public repository).");
                foreach (var key in OwnerKeys.Where(k => line.Contains($":{k}:", StringComparison.Ordinal)))
                {
                    // Pulumi writes secrets either inline or, as `pulumi config set --secret` does, on the next line.
                    var value = line.Contains("secure:", StringComparison.Ordinal) ? line : i + 1 < lines.Length ? lines[i + 1] : "";
                    value.TrimStart().ShouldStartWith("secure:", customMessage: $"{Path.GetFileName(file)}: {key} must be set with --secret.");
                }
            }
        }
    }
}

using Shouldly;

namespace UmbracoSample.Infra.Tests;

public sealed class SiteSettingsTests
{
    private const string Digest = "ghcr.io/example/umbraco-sample@sha256:1111111111111111111111111111111111111111111111111111111111111111";

    private static SiteSettings Valid(string environment = "dev") => new(environment, 0, 1, 20m, "owner@example.invalid", Digest, EnableEdge: false, "admin@example.invalid", "owners-sql", "00000000-0000-0000-0000-0000000000aa");

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

    [Theory]
    [InlineData("")]
    [InlineData("admin")]
    public void The_admin_login_must_be_an_email_address(string email) =>
        Should.Throw<ArgumentException>(() => (Valid() with { AdminEmail = email }).Validate()).Message.ShouldContain("adminEmail");

    [Fact]
    public void Scaling_out_is_refused_until_umbraco_can_share_state() =>
        Should.Throw<ArgumentException>(() => (Valid() with { MaxReplicas = 2 }).Validate()).Message.ShouldContain("DEV-015");

    [Fact]
    public void The_placeholder_digest_is_refused() =>
        Should.Throw<ArgumentException>(() => (Valid() with { Image = "ghcr.io/example/umbraco-sample@sha256:0000000000000000000000000000000000000000000000000000000000000000" }).Validate()).Message.ShouldContain("placeholder");

    [Theory]
    [InlineData("", "00000000-0000-0000-0000-0000000000aa")]
    [InlineData("owners-sql", "not-a-guid")]
    public void An_owner_group_must_administer_sql(string groupName, string objectId) =>
        Should.Throw<ArgumentException>(() => (Valid() with { SqlAdminGroupName = groupName, SqlAdminGroupObjectId = objectId }).Validate()).Message.ShouldContain("sqlAdminGroup");
}

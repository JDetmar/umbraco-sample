using System.Text.RegularExpressions;
using Shouldly;

namespace UmbracoSample.Repository.Tests;

/// <summary>
/// Executable supply-chain rules (ES-013, ES-017, ES-018): they fail the authoritative gate when a rule is broken.
/// </summary>
public sealed partial class SupplyChainPolicyTests
{
    private static readonly string[] NpmDependencySections = ["dependencies", "devDependencies"];

    [Fact]
    public void Third_party_actions_are_pinned_to_a_full_commit_sha()
    {
        var violations = WorkflowFiles()
            .SelectMany(file => UsesLines(file).Select(line => (file.Name, line)))
            .Where(entry => !IsLocalAction(entry.line) && !PinnedUses().IsMatch(entry.line))
            .Select(entry => $"{entry.Name}: {entry.line}")
            .ToList();

        violations.ShouldBeEmpty("ES-017: pin every third-party action as owner/repo@<40-char sha> # <tag>.");
    }

    [Fact]
    public void Every_workflow_declares_top_level_permissions()
    {
        var missing = WorkflowFiles()
            .Where(file => !File.ReadAllLines(file.FullName).Any(line => line.StartsWith("permissions:", StringComparison.Ordinal)))
            .Select(file => file.Name)
            .ToList();

        var broad = WorkflowFiles()
            .Where(file => File.ReadAllLines(file.FullName).Any(line => BroadPermissions().IsMatch(line)))
            .Select(file => file.Name)
            .ToList();

        missing.ShouldBeEmpty("ES-017: every workflow declares least-privilege top-level permissions.");
        broad.ShouldBeEmpty("ES-017: write-all and read-all grant more than any job needs; list each permission.");
    }

    [Fact]
    public void Write_permissions_are_granted_only_to_allowlisted_jobs()
    {
        // (workflow, job, permission): each write grant is reviewed here before it can exist (ES-017).
        (string, string, string)[] allowed = [("ci.yml", "publish", "packages"), ("codeql.yml", "analyze", "security-events"), ("infra.yml", "preview", "id-token"), ("ci.yml", "deploy-dev", "id-token"), ("restore-test.yml", "restore", "id-token")];

        var grants = new List<(string, string, string)>();
        foreach (var file in WorkflowFiles())
        {
            string? job = null;
            foreach (var line in File.ReadAllLines(file.FullName))
            {
                var jobMatch = JobHeader().Match(line);
                if (jobMatch.Success)
                {
                    job = jobMatch.Groups[1].Value;
                }

                var grant = WriteGrant().Match(line);
                if (grant.Success)
                {
                    grants.Add((file.Name, job ?? "<top level>", grant.Groups[1].Value));
                }
            }
        }

        grants.ShouldNotBeEmpty();
        grants.Except(allowed).ShouldBeEmpty("ES-017: a new write permission needs review; add it to this allowlist with a reason in the ledger.");
    }

    [Fact]
    public void The_dev_deploy_runs_only_from_main_in_the_dev_environment_one_at_a_time()
    {
        // The dev environment binds the OIDC subject the deploy identity trusts; dropping it, or the main-only
        // condition, would let other runs ask for the deploy identity's token (ADR 0013, threat model 0002 R4).
        var job = JobBlock("ci.yml", "deploy-dev");

        job.ShouldContain("    needs: publish");
        job.ShouldContain("    environment: dev");
        job.ShouldContain("github.ref == 'refs/heads/main'");
        job.ShouldContain("      group: pulumi-dev");
        job.ShouldContain("      cancel-in-progress: false");
        job.ShouldContain("persist-credentials: false");
        job.ShouldContain("steps.current.outputs.deploy == 'true'");
        JobBlock("infra.yml", "preview").ShouldContain("      group: pulumi-dev");
    }

    [Fact]
    public void Pulumi_runs_in_ci_never_print_a_property_diff()
    {
        // Actions logs are public; a Pulumi property diff prints systemData, which holds the sign-in name of whoever
        // created or last changed a resource (ADR 0014). PR comments and job summaries are public too.
        var offenders = WorkflowFiles()
            .SelectMany(file => File.ReadAllLines(file.FullName).Select(line => (file.Name, Line: line.Trim())))
            .Where(entry => !entry.Line.StartsWith('#') && PrintsPulumiProperties().IsMatch(entry.Line))
            .Select(entry => $"{entry.Name}: {entry.Line}")
            .ToList();

        offenders.ShouldBeEmpty("Pulumi steps in CI keep diff: false and post nothing to comments or summaries; use Pulumi Cloud or `make infra-preview` for a full diff.");
    }

    [Theory]
    [InlineData("diff: true")]
    [InlineData("diff: 'true'")]
    [InlineData("diff: \"True\"")]
    [InlineData("diff: ${{ inputs.diff }}")]
    [InlineData("comment-on-pr: true")]
    [InlineData("comment-on-summary: true")]
    [InlineData("always-include-summary: true")]
    [InlineData("run: pulumi preview --diff")]
    [InlineData("run: pulumi up --yes --show-secrets")]
    public void The_property_diff_check_catches_each_way_of_turning_it_on(string line) =>
        PrintsPulumiProperties().IsMatch(line).ShouldBeTrue();

    [Theory]
    [InlineData("diff: false")]
    [InlineData("comment-on-pr: false")]
    [InlineData("run: git diff --exit-code")]
    public void The_property_diff_check_allows_the_safe_settings(string line) =>
        PrintsPulumiProperties().IsMatch(line).ShouldBeFalse();

    // Any value other than a literal false turns the input on, including quoted or expression values.
    [GeneratedRegex(@"^(?:diff|comment-on-pr|comment-on-summary|always-include-summary)\s*:\s*(?!false\s*$)\S|pulumi\b.*\s--(?:diff|show-secrets)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PrintsPulumiProperties();

    [Fact]
    public void The_restore_test_runs_only_the_reviewed_script_on_main()
    {
        // It borrows the deploy identity (DEV-017, ADR 0019); everything it does to Azure is in the reviewed script.
        var lines = JobBlock("restore-test.yml", "restore").Split('\n');

        lines.ShouldContain("    if: vars.CD_DEV_ENABLED == 'true' && github.ref == 'refs/heads/main'");
        lines.ShouldContain("    environment: dev");
        lines.ShouldContain("          persist-credentials: false");
        lines.ShouldContain("      cancel-in-progress: false");
        lines.ShouldNotContain("      group: pulumi-dev");
        lines.Where(line => line.TrimStart().StartsWith("command:", StringComparison.Ordinal)).ShouldBe(["          command: output"]);
        lines.Where(line => line.TrimStart().StartsWith("run:", StringComparison.Ordinal)).ShouldBe(["        run: bash infra/UmbracoSample.Infra/scripts/restore-test.sh"]);
        // No other step can act on Azure: only these actions, in this order.
        lines.Where(line => line.TrimStart().StartsWith("- uses:", StringComparison.Ordinal)).Select(line => line.Trim()[8..line.Trim().IndexOf('@', StringComparison.Ordinal)])
            .ShouldBe(["actions/checkout", "azure/login", "pulumi/auth-actions", "pulumi/actions"]);
        lines.ShouldNotContain(line => line.Contains("inlineScript", StringComparison.Ordinal) || line.Contains("shell:", StringComparison.Ordinal));
    }

    private static string JobBlock(string workflow, string job)
    {
        var lines = File.ReadAllLines(Path.Join(RepositoryRoot.Find().FullName, ".github", "workflows", workflow));
        var start = Array.IndexOf(lines, $"  {job}:");
        start.ShouldBeGreaterThanOrEqualTo(0, $"{workflow} has no job {job}");
        var end = Array.FindIndex(lines, start + 1, line => JobHeader().IsMatch(line));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }

    [Theory]
    [InlineData("frontend")]
    [InlineData("e2e")]
    public void Npm_dependencies_are_exact_and_locked(string folder)
    {
        var root = RepositoryRoot.Find().FullName;
        using var package = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Join(root, folder, "package.json")));

        var ranges = NpmDependencySections
            .Where(section => package.RootElement.TryGetProperty(section, out _))
            .SelectMany(section => package.RootElement.GetProperty(section).EnumerateObject())
            .Where(dependency => !ExactVersion().IsMatch(dependency.Value.GetString() ?? string.Empty))
            .Select(dependency => $"{dependency.Name}@{dependency.Value.GetString()}")
            .ToList();

        ranges.ShouldBeEmpty("ES-013: npm dependencies are pinned to exact versions.");
        RepositoryRoot.TrackedFiles().ShouldContain($"{folder}/package-lock.json", "ES-013: the npm lock file is committed.");
    }

    [Fact]
    public void The_playwright_image_is_pinned_by_digest_and_matches_the_library()
    {
        var root = RepositoryRoot.Find().FullName;
        var from = File.ReadAllLines(Path.Join(root, "e2e", "Dockerfile")).Single(line => line.StartsWith("FROM ", StringComparison.Ordinal));
        using var package = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Join(root, "e2e", "package.json")));
        var library = package.RootElement.GetProperty("devDependencies").GetProperty("@playwright/test").GetString();

        var match = PlaywrightImage().Match(from);
        match.Success.ShouldBeTrue("ES-013: the Playwright image is pinned by tag and sha256 digest.");
        match.Groups["version"].Value.ShouldBe(library, "the browsers in the image must match @playwright/test; update both together.");
    }

    [Fact]
    public void Npm_never_runs_install_scripts_and_enforces_the_node_version()
    {
        var root = RepositoryRoot.Find().FullName;
        var frontend = File.ReadAllLines(Path.Join(root, "frontend", ".npmrc"));
        var e2e = File.ReadAllLines(Path.Join(root, "e2e", ".npmrc"));

        frontend.ShouldContain("ignore-scripts=true", "ES-013: dependency install scripts are arbitrary code; none are needed.");
        frontend.ShouldContain("engine-strict=true");
        frontend.ShouldContain("save-exact=true");
        // e2e runs on the Node inside the pinned Playwright image, so it pins the image rather than the engine.
        e2e.ShouldContain("ignore-scripts=true");
        e2e.ShouldContain("save-exact=true");
    }

    [Fact]
    public void Node_engine_range_matches_the_ci_version()
    {
        var root = RepositoryRoot.Find().FullName;
        var ci = File.ReadAllText(Path.Join(root, ".nvmrc")).Trim();
        using var package = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Join(root, "frontend", "package.json")));
        var engine = package.RootElement.GetProperty("engines").GetProperty("node").GetString() ?? string.Empty;
        using var lockFile = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Join(root, "frontend", "package-lock.json")));
        var lockedEngine = lockFile.RootElement.GetProperty("packages").GetProperty("").GetProperty("engines").GetProperty("node").GetString();

        ReleaseVersion().IsMatch(ci).ShouldBeTrue("ES-013: CI installs an exact, non-prerelease Node version from .nvmrc.");
        var range = NodeEngineRange().Match(engine);
        range.Success.ShouldBeTrue("engines is '>=<first LTS of the major> <<major + 1>.0.0' (ADR 0007).");
        var ciVersion = Version.Parse(ci);
        var lower = Version.Parse(range.Groups["lower"].Value);
        lower.Major.ShouldBe(ciVersion.Major, "engines and .nvmrc are on the same Node major.");
        (lower <= ciVersion).ShouldBeTrue(".nvmrc lies inside the engines range.");
        range.Groups["upper"].Value.ShouldBe((ciVersion.Major + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "the range stops at the next major.");
        lockedEngine.ShouldBe(engine, "package-lock.json records the same engines as package.json; regenerate it.");

        var setupNodeSteps = WorkflowFiles().SelectMany(file => File.ReadAllLines(file.FullName)).Count(line => line.Contains("uses: actions/setup-node@", StringComparison.Ordinal));
        var readNvmrc = WorkflowFiles().SelectMany(file => File.ReadAllLines(file.FullName)).Count(line => line.Trim() == "node-version-file: .nvmrc");
        setupNodeSteps.ShouldBeGreaterThan(0);
        readNvmrc.ShouldBe(setupNodeSteps, "every CI setup-node step installs the exact version in .nvmrc.");
    }

    [Fact]
    public void Package_versions_are_only_set_centrally()
    {
        var violations = RepositoryRoot.Files("*.csproj")
            .Where(file => InlinePackageVersion().IsMatch(File.ReadAllText(file.FullName)))
            .Select(file => file.Name)
            .ToList();

        violations.ShouldBeEmpty("ES-013: package versions belong in Directory.Packages.props only.");
    }

    [Fact]
    public void Every_project_has_a_committed_lock_file()
    {
        // Checks what Git tracks, not what is on disk: restore recreates a missing lock file, so a disk check could never fail.
        var tracked = RepositoryRoot.TrackedFiles().ToHashSet(StringComparer.Ordinal);
        var root = RepositoryRoot.Find().FullName;

        var missing = RepositoryRoot.Files("*.csproj")
            .Select(file => Path.GetRelativePath(root, Path.Join(file.DirectoryName!, "packages.lock.json")).Replace('\\', '/'))
            .Where(lockFile => !tracked.Contains(lockFile))
            .ToList();

        missing.ShouldBeEmpty("ES-013: every project restores from a lock file committed to Git.");
    }

    [Fact]
    public void Committed_app_settings_hold_no_image_signing_key()
    {
        // Umbraco writes a generated HMACSecretKey into appsettings.json on first install when none is configured.
        // Keys come from user secrets locally and Key Vault in Azure (ES-024).
        var violations = RepositoryRoot.Files("appsettings*.json")
            .Where(file => ImageSigningKeyValue().IsMatch(File.ReadAllText(file.FullName)))
            .Select(file => file.Name)
            .ToList();

        violations.ShouldBeEmpty("ES-024: secrets stay out of committed configuration.");
    }

    [Fact]
    public void Pin_check_rejects_tags_and_branches()
    {
        PinnedUses().IsMatch("uses: actions/checkout@v4").ShouldBeFalse();
        PinnedUses().IsMatch("uses: actions/checkout@main").ShouldBeFalse();
        PinnedUses().IsMatch("uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1").ShouldBeTrue();
    }

    [Fact]
    public void Azure_tenant_and_subscription_ids_reach_workflows_only_as_secrets()
    {
        // Actions logs are public. A variable's value is printed wherever a step echoes its inputs or env, and the tenant
        // ID resolves to the tenant's default domain. Secrets are masked everywhere in the log (ES-025).
        var offenders = WorkflowFiles()
            .SelectMany(file => File.ReadAllLines(file.FullName).Select((line, i) => (file.Name, Line: i + 1, Text: line)))
            .Where(l => ExposesAzureId(l.Text))
            .Select(l => $"{l.Name}:{l.Line}")
            .ToList();

        offenders.ShouldBeEmpty("Every tenant or subscription ID input must come from secrets (not vars, env or a literal), so the public log shows ***.");
    }

    [Theory]
    [InlineData("tenant-id: ${{ vars.AZURE_TENANT_ID }}", true)]
    [InlineData("ARM_SUBSCRIPTION_ID: ${{ vars.AZURE_SUBSCRIPTION_ID }}", true)]
    [InlineData("tenant-id: ${{ vars['AZURE_TENANT_ID'] }}", true)]
    [InlineData("subscription-id: ${{ VARS.azure_subscription_id }}", true)]
    [InlineData("ARM_TENANT_ID: ${{ vars.TENANT }}", true)]
    [InlineData("tenant-id: ${{ env.TENANT }}", true)]
    [InlineData("ARM_TENANT_ID: 00000000-0000-0000-0000-000000000000", true)]
    [InlineData("  AZURE_TENANT_ID: ${{ vars.AZURE_TENANT_ID }}", true)]
    [InlineData("tenant-id: ${{ secrets.AZURE_TENANT_ID }}", false)]
    [InlineData("ARM_SUBSCRIPTION_ID: ${{ Secrets['AZURE_SUBSCRIPTION_ID'] }}", false)]
    [InlineData("client-id: ${{ vars.AZURE_DEPLOY_CLIENT_ID }}", false)]
    [InlineData("# the tenant-id input is read from secrets", false)]
    public void The_azure_id_check_accepts_only_secrets(string line, bool flagged) =>
        ExposesAzureId(line).ShouldBe(flagged);

    // A line that sets a tenant or subscription ID (an action input or an env key), or reads one by name from any
    // context, must take it from secrets. Contexts and names are case-insensitive in Actions.
    private static bool ExposesAzureId(string line)
    {
        var code = line.TrimStart();
        if (code.StartsWith('#'))
        {
            return false;
        }

        return (AzureIdKey().IsMatch(code) || AzureIdName().IsMatch(code)) && !FromSecrets().IsMatch(code);
    }

    [GeneratedRegex(@"^(?:tenant-id|subscription-id|[A-Z_]*(?:TENANT|SUBSCRIPTION)_ID)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex AzureIdKey();

    [GeneratedRegex(@"(?:TENANT|SUBSCRIPTION)_ID", RegexOptions.IgnoreCase)]
    private static partial Regex AzureIdName();

    [GeneratedRegex(@"\$\{\{\s*secrets(?:\.[A-Z_]*(?:TENANT|SUBSCRIPTION)_ID|\[\s*'[A-Z_]*(?:TENANT|SUBSCRIPTION)_ID'\s*\])\s*\}\}\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FromSecrets();

    private static IEnumerable<FileInfo> WorkflowFiles() =>
        new DirectoryInfo(Path.Join(RepositoryRoot.Find().FullName, ".github", "workflows"))
            .EnumerateFiles("*.y*ml");

    private static IEnumerable<string> UsesLines(FileInfo file) =>
        File.ReadAllLines(file.FullName)
            .Select(line => line.Trim().TrimStart('-').Trim())
            .Where(line => line.StartsWith("uses:", StringComparison.Ordinal));

    private static bool IsLocalAction(string usesLine) => usesLine.Contains("uses: ./", StringComparison.Ordinal);

    [GeneratedRegex(@"^uses:\s+[\w.-]+/[\w./-]+@[0-9a-f]{40}\s+#\s+\S+")]
    private static partial Regex PinnedUses();

    // A string value, not the JSON schema's object definition of the same property.
    [GeneratedRegex(@"""HMACSecretKey""\s*:\s*""[^""]+""", RegexOptions.IgnoreCase)]
    private static partial Regex ImageSigningKeyValue();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex ExactVersion();

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex ReleaseVersion();

    [GeneratedRegex(@"^>=(?<lower>\d+\.\d+\.\d+) <(?<upper>\d+)\.0\.0$")]
    private static partial Regex NodeEngineRange();

    [GeneratedRegex(@"^FROM mcr\.microsoft\.com/playwright:v(?<version>\d+\.\d+\.\d+)-noble@sha256:[0-9a-f]{64}$")]
    private static partial Regex PlaywrightImage();

    [GeneratedRegex(@"^  ([A-Za-z0-9_-]+):\s*$")]
    private static partial Regex JobHeader();

    [GeneratedRegex(@"^\s+([a-z-]+):\s*write\s*(#.*)?$")]
    private static partial Regex WriteGrant();

    [GeneratedRegex(@"permissions:\s*(write-all|read-all)")]
    private static partial Regex BroadPermissions();

    [GeneratedRegex(@"<PackageReference\s[^>]*Version\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InlinePackageVersion();
}

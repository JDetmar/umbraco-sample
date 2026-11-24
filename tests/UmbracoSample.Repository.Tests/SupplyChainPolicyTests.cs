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

    [GeneratedRegex(@"permissions:\s*(write-all|read-all)")]
    private static partial Regex BroadPermissions();

    [GeneratedRegex(@"<PackageReference\s[^>]*Version\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InlinePackageVersion();
}

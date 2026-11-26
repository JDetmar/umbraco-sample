using System.Xml.Linq;
using Shouldly;

namespace UmbracoSample.Repository.Tests;

/// <summary>
/// The shared libraries stay shared (ADR 0020, ES-018): none depends on a site host, and each takes only the packages
/// its job needs, so a second host can reuse them without inheriting this site.
/// </summary>
public sealed class SharedLibraryPolicyTests
{
    /// <summary>Each shared library and the only packages it may reference directly.</summary>
    public static TheoryData<string, string[]> SharedLibraries => new()
    {
        { "UmbracoSample.Domain", [] },
        { "UmbracoSample.Security", [] },
        { "UmbracoSample.Components", ["Umbraco.Cms.Web.Common"] },
        { "UmbracoSample.Governance", ["Umbraco.Cms.Core"] },
    };

    [Theory]
    [MemberData(nameof(SharedLibraries))]
    public void A_shared_library_references_no_host_and_only_its_own_packages(string library, string[] allowedPackages)
    {
        var project = Project(library);

        var shared = SharedLibraries.Select(row => row.Data.Item1).ToHashSet(StringComparer.Ordinal);
        project.Descendants("ProjectReference").Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')))
            .ShouldAllBe(name => shared.Contains(name),
                $"{library} may reference only other shared libraries, never a host such as UmbracoSample.Web.");
        project.Descendants("PackageReference").Select(reference => (string)reference.Attribute("Include")!)
            .ShouldBeSubsetOf(allowedPackages, $"{library} takes a new package only with a reason in the pull request (ES-015).");
    }

    [Fact]
    public void The_umbraco_packages_the_libraries_use_move_with_umbraco_itself()
    {
        var versions = XDocument.Load(Path.Join(RepositoryRoot.Find().FullName, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .ToDictionary(entry => (string)entry.Attribute("Include")!, entry => (string)entry.Attribute("Version")!);

        // They are parts of the approved Umbraco.Cms (docs/dependencies.md), so they never drift from its version.
        versions["Umbraco.Cms.Core"].ShouldBe(versions["Umbraco.Cms"]);
        versions["Umbraco.Cms.Web.Common"].ShouldBe(versions["Umbraco.Cms"]);
    }

    private static XDocument Project(string name) =>
        XDocument.Load(Path.Join(RepositoryRoot.Find().FullName, "src", name, $"{name}.csproj"));
}

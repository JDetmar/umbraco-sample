using System.Xml.Linq;
using NetArchTest.Rules;
using Shouldly;
using UmbracoSample.Domain.Redirects;

namespace UmbracoSample.Domain.Tests;

/// <summary>ES-018, ES-020: domain logic never depends on the CMS, the web framework, infrastructure or I/O.</summary>
public sealed class ArchitectureTests
{
    private static readonly string[] Forbidden =
    [
        "Umbraco",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data",
        "System.Data",
        "System.Net",
        "System.IO",
    ];

    [Fact]
    public void The_domain_depends_on_no_framework_infrastructure_or_io()
    {
        // Coverage collection in `make test` adds one tracker type to the instrumented assembly; it is not domain code.
        var result = Rule(Types.InAssembly(typeof(RedirectMap).Assembly).That().DoNotResideInNamespace(CoverageTrackerNamespace)).GetResult();

        result.IsSuccessful.ShouldBeTrue($"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void The_rule_catches_a_violation()
    {
        // A committed negative test: the same rule applied to a type in this assembly that uses I/O and HTTP.
        var result = Rule(Types.InAssembly(typeof(ViolatingProbe).Assembly).That().HaveName(nameof(ViolatingProbe))).GetResult();

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(ViolatingProbe).FullName);
    }

    [Fact]
    public void The_domain_project_references_no_packages_or_projects()
    {
        var project = XDocument.Load(Path.Join(RepositoryRoot(), "src", "UmbracoSample.Domain", "UmbracoSample.Domain.csproj"));
        var references = project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference" or "Reference" or "FrameworkReference")
            .Select(element => (string?)element.Attribute("Include"))
            .ToList();

        references.ShouldBeEmpty("ES-020: the domain uses only the base class library.");
    }

    private const string CoverageTrackerNamespace = "Coverlet.Core.Instrumentation.Tracker";

    [Fact]
    public void No_domain_code_hides_in_the_coverage_tracker_namespace() =>
        Directory.EnumerateFiles(Path.Join(RepositoryRoot(), "src", "UmbracoSample.Domain"), "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("Coverlet", StringComparison.Ordinal))
            .ShouldBeEmpty();

    private static ConditionList Rule(PredicateList types) => types.ShouldNot().HaveDependencyOnAny(Forbidden);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "UmbracoSample.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>Deliberately breaks the domain rule; used only by <see cref="ArchitectureTests.The_rule_catches_a_violation"/>.</summary>
internal static class ViolatingProbe
{
    public static string Read(string path) => File.ReadAllText(path);

    public static HttpClient Client() => new();
}

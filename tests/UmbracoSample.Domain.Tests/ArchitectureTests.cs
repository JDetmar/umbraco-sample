using NetArchTest.Rules;
using Shouldly;
using UmbracoSample.Domain.Redirects;

namespace UmbracoSample.Domain.Tests;

/// <summary>ES-018, ES-020: domain logic never depends on the CMS, the web framework or infrastructure.</summary>
public sealed class ArchitectureTests
{
    private static readonly string[] Forbidden =
    [
        "Umbraco",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data",
        "System.Net.Http",
    ];

    [Fact]
    public void The_domain_depends_on_no_framework_or_infrastructure()
    {
        var result = Types.InAssembly(typeof(RedirectMap).Assembly).ShouldNot().HaveDependencyOnAny(Forbidden).GetResult();

        result.IsSuccessful.ShouldBeTrue($"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void The_domain_project_references_no_packages_or_projects()
    {
        var references = typeof(RedirectMap).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToList();

        references.ShouldAllBe(name => name != null && (name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard"), $"References: {string.Join(", ", references)}");
    }
}

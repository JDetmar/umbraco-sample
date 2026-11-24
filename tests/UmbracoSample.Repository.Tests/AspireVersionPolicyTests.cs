using System.Text.Json;
using System.Xml.Linq;
using Shouldly;

namespace UmbracoSample.Repository.Tests;

/// <summary>
/// Aspire 17 builds through the pinned Aspire CLI local tool (ADR 0012, ES-013). The AppHost points at that tool's
/// package by version, so the SDK, the hosting packages, the tool manifest and the AppHost's CLI version must agree.
/// </summary>
public sealed class AspireVersionPolicyTests
{
    [Fact]
    public void Aspire_cli_tool_sdk_and_packages_share_one_version()
    {
        var root = RepositoryRoot.Find().FullName;

        using var tools = JsonDocument.Parse(File.ReadAllText(Path.Join(root, ".config", "dotnet-tools.json")));
        var cliTool = tools.RootElement.GetProperty("tools").GetProperty("aspire.cli");
        var toolVersion = cliTool.GetProperty("version").GetString();
        cliTool.GetProperty("rollForward").GetBoolean().ShouldBeFalse("ES-013: the Aspire CLI tool does not roll forward.");

        var appHost = XDocument.Load(Path.Join(root, "src", "UmbracoSample.AppHost", "UmbracoSample.AppHost.csproj"));
        var sdk = appHost.Root!.Attribute("Sdk")!.Value;
        var cliVersion = appHost.Descendants("AspireCliVersion").Single().Value;

        using var global = JsonDocument.Parse(File.ReadAllText(Path.Join(root, "global.json")));
        var globalSdk = global.RootElement.GetProperty("msbuild-sdks").GetProperty("Aspire.AppHost.Sdk").GetString();

        var packages = XDocument.Load(Path.Join(root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Where(p => p.Attribute("Include")!.Value.StartsWith("Aspire.", StringComparison.Ordinal))
            .Select(p => (Name: p.Attribute("Include")!.Value, Version: p.Attribute("Version")!.Value))
            .ToList();

        toolVersion.ShouldNotBeNullOrEmpty();
        cliVersion.ShouldBe(toolVersion, "AspireCliVersion in the AppHost must match the aspire.cli tool version.");
        sdk.ShouldBe($"Aspire.AppHost.Sdk/{toolVersion}", "The AppHost SDK must match the Aspire CLI version.");
        globalSdk.ShouldBe(toolVersion, "global.json msbuild-sdks must match the Aspire CLI version.");
        packages.ShouldNotBeEmpty();
        packages.Where(p => p.Version != toolVersion).ShouldBeEmpty("Every Aspire.* package version must match the Aspire CLI version.");
    }
}

using Microsoft.Extensions.Configuration;
using Shouldly;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Extensions;

namespace UmbracoSample.Web.Tests.Security;

/// <summary>
/// Media is served from this site's origin, which the content security policy trusts as 'self'.
/// Pins the upload allowlist so an editor cannot upload a file the browser would run as script (DEV-007, ADR 0008).
/// </summary>
public sealed class UploadPolicyTests
{
    private static readonly ContentSettings Settings = Load();

    private static ContentSettings Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var settings = new ContentSettings();
        configuration.GetSection("Umbraco:CMS:Content").Bind(settings);
        return settings;
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("pdf")]
    public void Ordinary_media_can_be_uploaded(string extension) =>
        Settings.IsFileAllowedForUpload(extension).ShouldBeTrue();

    [Theory]
    [InlineData("js")]
    [InlineData("mjs")]
    [InlineData("html")]
    [InlineData("htm")]
    [InlineData("xhtml")]
    [InlineData("svg")]
    [InlineData("svgz")]
    [InlineData("xml")]
    [InlineData("css")]
    [InlineData("cshtml")]
    [InlineData("config")]
    [InlineData("JS")]
    [InlineData("exe")]
    [InlineData("txt")]
    public void Files_a_browser_could_run_or_the_server_could_execute_are_refused(string extension) =>
        Settings.IsFileAllowedForUpload(extension).ShouldBeFalse();

    [Fact]
    public void Uploads_are_an_allowlist_not_only_a_denylist() =>
        Settings.AllowedUploadedFileExtensions.ShouldNotBeEmpty("ES-026: an unknown extension must be refused by default.");
}

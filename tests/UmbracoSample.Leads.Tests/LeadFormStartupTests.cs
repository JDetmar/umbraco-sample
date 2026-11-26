using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace UmbracoSample.Leads.Tests;

/// <summary>Threat model 0001, D1: the CAPTCHA is off by default, and on without a verifier the site does not start.</summary>
public sealed class LeadFormStartupTests
{
    private static IHost Build(bool? captcha)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var settings = new Dictionary<string, string?>
        {
            ["Leads:Map:BaseAddress"] = "http://127.0.0.1:9/",
            ["Leads:Map:ApiKey"] = "test-key-not-a-secret",
        };
        if (captcha is { } on)
        {
            settings["Leads:Captcha:Enabled"] = on ? "true" : "false";
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddLeadForm(builder.Configuration);
        return builder.Build();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task The_site_starts_with_the_captcha_off(bool? captcha)
    {
        using var host = Build(captcha);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task With_the_captcha_on_and_no_verifier_the_site_refuses_to_start()
    {
        using var host = Build(captcha: true);

        var failure = await Should.ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
        failure.Message.ShouldContain("no CAPTCHA verifier");
    }
}

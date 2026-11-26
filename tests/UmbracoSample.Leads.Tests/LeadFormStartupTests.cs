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
            settings["Forms:Captcha:Enabled"] = on ? "true" : "false";
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
    public async Task With_the_captcha_on_and_a_scoped_verifier_the_site_starts()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Leads:Map:BaseAddress"] = "http://127.0.0.1:9/",
            ["Leads:Map:ApiKey"] = "test-key-not-a-secret",
            ["Forms:Captcha:Enabled"] = "true",
        });
        builder.Services.AddLeadForm(builder.Configuration);
        builder.Services.AddScoped<ICaptchaVerifier, PassingVerifier>();
        builder.Services.AddSingleton<ILeadFormCatalog, NoForms>(); // the host supplies the catalog
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));
        using var host = builder.Build();
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

    private sealed class NoForms : ILeadFormCatalog
    {
        public LeadFormSettings? Find(Guid blockKey) => null;
    }

    private sealed class PassingVerifier : ICaptchaVerifier
    {
        public Task<bool> VerifyAsync(string? response, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}

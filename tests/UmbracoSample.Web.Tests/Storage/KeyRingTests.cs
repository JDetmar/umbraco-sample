using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using UmbracoSample.Web.Storage;

namespace UmbracoSample.Web.Tests.Storage;

/// <summary>The shared Data Protection key ring (ADR 0021; threat model 0001, R3). No test reaches Azure.</summary>
public sealed class KeyRingTests
{
    private const string Blob = "https://account.blob.core.windows.net/dataprotection/keys.xml";
    private const string Key = "https://vault.vault.azure.net/keys/dataprotection";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static ServiceProvider Services(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyRing(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Without_settings_the_framework_default_store_is_kept()
    {
        using var services = Services(Config());

        KeyRing.IsRequested(Config()).ShouldBeFalse();
        services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository.ShouldBeNull();
    }

    [Fact]
    public void With_both_settings_keys_live_in_the_blob_and_are_wrapped_by_the_key_vault_key()
    {
        using var services = Services(Config((KeyRing.BlobUriKey, Blob), (KeyRing.KeyUriKey, Key)));
        var options = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.ShouldNotBeNull().GetType().Name.ShouldBe("AzureBlobXmlRepository");
        options.XmlEncryptor.ShouldNotBeNull().GetType().Name.ShouldBe("AzureKeyVaultXmlEncryptor", "keys at rest are wrapped by the Key Vault key, never stored in plain form");
    }

    [Fact]
    public void Every_replica_uses_the_same_application_name()
    {
        using var services = Services(Config());

        services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator.ShouldBe(KeyRing.ApplicationName);
    }

    [Theory]
    [InlineData(Blob, null)]
    [InlineData(null, Key)]
    [InlineData("https://account.blob.core.windows.net/dataprotection", Key)]
    [InlineData("https://account.blob.core.windows.net/dataprotection/keys.xml?sv=2024-01-01&sig=abc", Key)]
    [InlineData("http://account.blob.core.windows.net/dataprotection/keys.xml", Key)]
    [InlineData("https://user:secret@account.blob.core.windows.net/dataprotection/keys.xml", Key)]
    [InlineData(Blob, "https://vault.vault.azure.net/secrets/dataprotection")]
    [InlineData(Blob, "https://vault.vault.azure.net/keys/")]
    [InlineData(Blob, "http://vault.vault.azure.net/keys/dataprotection")]
    [InlineData(Blob, "https://vault.vault.azure.net/keys/dataprotection/0123456789abcdef0123456789abcdef")]
    [InlineData(Blob, "https://vault.vault.azure.net/keys/dataprotection/")]
    [InlineData(Blob, "https://vault.vault.azure.net/keys/dataprotection#v1")]
    [InlineData(Blob + "#x", Key)]
    public void Startup_refuses_half_a_configuration_a_sas_token_plain_http_or_a_non_key_uri(string? blob, string? key)
    {
        var settings = new List<(string, string)>();
        if (blob is not null)
        {
            settings.Add((KeyRing.BlobUriKey, blob));
        }

        if (key is not null)
        {
            settings.Add((KeyRing.KeyUriKey, key));
        }

        Should.Throw<InvalidOperationException>(() => Services(Config([.. settings])));
    }
}

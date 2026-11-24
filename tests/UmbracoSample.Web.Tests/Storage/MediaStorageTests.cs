using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Umbraco.StorageProviders.AzureBlob.IO;
using UmbracoSample.Web.Storage;

namespace UmbracoSample.Web.Tests.Storage;

public sealed class MediaStorageTests
{
    private const string Section = "Umbraco:Storage:AzureBlob:Media";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    /// <summary>Resolves the provider's options the way Umbraco does (bound, configured, validated), with the site's rules applied.</summary>
    private static AzureBlobFileSystemOptions Resolve(IConfiguration configuration)
    {
        var credential = WorkloadIdentity.Credential(configuration);
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddOptions<AzureBlobFileSystemOptions>(AzureBlobFileSystemOptions.MediaFileSystemName)
            .BindConfiguration(Section)
            .Configure(o => o.VirtualPath = "~/media")
            .Configure(o => MediaStorage.Configure(o, credential))
            .ValidateDataAnnotations();
        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<AzureBlobFileSystemOptions>>().Get(AzureBlobFileSystemOptions.MediaFileSystemName);
    }

    [Fact]
    public void Without_an_endpoint_media_stays_on_local_disk() =>
        MediaStorage.IsConfigured(Config()).ShouldBeFalse();

    [Fact]
    public void An_endpoint_switches_media_to_blob_storage() =>
        MediaStorage.IsConfigured(Config((MediaStorage.EndpointKey, "https://account.blob.core.windows.net/"))).ShouldBeTrue();

    [Theory]
    [InlineData("https://account.blob.core.windows.net/", "media")]
    [InlineData("https://account.blob.core.windows.net", "media")]
    [InlineData("https://account.blob.core.windows.net/", null)]
    public void The_wired_client_targets_the_container_with_a_token_credential(string endpoint, string? container)
    {
        var configuration = container is null
            ? Config((MediaStorage.EndpointKey, endpoint))
            : Config((MediaStorage.EndpointKey, endpoint), (MediaStorage.ContainerKey, container));

        var client = Resolve(configuration).CreateBlobContainerClient();

        client.Uri.AbsoluteUri.TrimEnd('/').ShouldBe("https://account.blob.core.windows.net/media");
        client.CanGenerateSasUri.ShouldBeFalse("ES-024: the site holds no account key, so it cannot mint SAS tokens.");
    }

    [Theory]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=a;AccountKey=a2V5;EndpointSuffix=core.windows.net")]
    [InlineData("http://account.blob.core.windows.net/")]
    [InlineData("https://account.blob.core.windows.net/?sv=2024-01-01&sig=abc")]
    [InlineData("https://user:secret@account.blob.core.windows.net/")]
    public void Startup_refuses_a_key_sas_or_plain_http_endpoint(string endpoint) =>
        Should.Throw<InvalidOperationException>(() => MediaStorage.ValidateEndpoint(endpoint)).Message.ShouldContain("without a key or SAS token");

    [Fact]
    public void Each_identity_gets_one_shared_credential()
    {
        var userAssigned = Config((WorkloadIdentity.ClientIdKey, "00000000-0000-0000-0000-00000000000a"));

        WorkloadIdentity.Credential(userAssigned).ShouldBeSameAs(WorkloadIdentity.Credential(userAssigned));
        WorkloadIdentity.Credential(userAssigned).ShouldNotBeSameAs(WorkloadIdentity.Credential(Config()));
        WorkloadIdentity.Credential(Config()).ShouldBeAssignableTo<TokenCredential>();
    }
}

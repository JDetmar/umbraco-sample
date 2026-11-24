using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shouldly;
using UmbracoSample.Web.Storage;

namespace UmbracoSample.Web.Tests.Storage;

public sealed class MediaStorageTests
{
    private static readonly TokenCredential Credential = WorkloadIdentity.Credential(new ConfigurationBuilder().Build());

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => KeyValuePair.Create(v.Key, (string?)v.Value))).Build();

    [Fact]
    public void Without_an_endpoint_media_stays_on_local_disk() =>
        MediaStorage.IsConfigured(Config()).ShouldBeFalse();

    [Fact]
    public void An_endpoint_switches_media_to_blob_storage() =>
        MediaStorage.IsConfigured(Config((MediaStorage.EndpointKey, "https://account.blob.core.windows.net/"))).ShouldBeTrue();

    [Theory]
    [InlineData("https://account.blob.core.windows.net/")]
    [InlineData("https://account.blob.core.windows.net")]
    public void The_container_client_targets_the_media_container_with_a_token_credential(string endpoint)
    {
        var client = MediaStorage.CreateContainerClient(endpoint, "media", Credential);

        client.Uri.ShouldBe(new Uri("https://account.blob.core.windows.net/media/"));
        client.Name.ShouldBe("media");
        client.CanGenerateSasUri.ShouldBeFalse("ES-024: the site holds no account key, so it cannot mint SAS tokens.");
    }

    [Theory]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=a;AccountKey=a2V5;EndpointSuffix=core.windows.net")]
    [InlineData("http://account.blob.core.windows.net/")]
    [InlineData("https://account.blob.core.windows.net/?sv=2024-01-01&sig=abc")]
    [InlineData("")]
    public void A_key_sas_or_plain_http_endpoint_is_refused(string endpoint) =>
        Should.Throw<InvalidOperationException>(() => MediaStorage.CreateContainerClient(endpoint, "media", Credential))
            .Message.ShouldContain("without a key or SAS token");
}

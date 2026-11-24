using Azure.Core;
using Azure.Storage.Blobs;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.StorageProviders.AzureBlob.IO;

namespace UmbracoSample.Web.Storage;

/// <summary>
/// Media lives in Blob storage when a container is configured (Azure), and on local disk otherwise (development, CI).
/// The site reaches the container with its managed identity; the storage account refuses shared keys (ADR 0010).
/// </summary>
public static class MediaStorage
{
    /// <summary>The blob service endpoint, such as <c>https://account.blob.core.windows.net/</c>. Never a connection string with a key.</summary>
    public const string EndpointKey = "Umbraco:Storage:AzureBlob:Media:ConnectionString";

    public const string ContainerKey = "Umbraco:Storage:AzureBlob:Media:ContainerName";

    public static bool IsConfigured(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return !string.IsNullOrWhiteSpace(configuration[EndpointKey]);
    }

    public static BlobContainerClient CreateContainerClient(string endpoint, string containerName, TokenCredential credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentNullException.ThrowIfNull(credential);

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var service) || service.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(service.Query))
        {
            throw new InvalidOperationException($"{EndpointKey} must be an https blob service endpoint without a key or SAS token.");
        }

        var container = new Uri(service, containerName.Trim('/') + "/");
        return new BlobContainerClient(container, credential);
    }

    public static IUmbracoBuilder AddMediaStorage(this IUmbracoBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!IsConfigured(configuration))
        {
            return builder;
        }

        var credential = WorkloadIdentity.Credential(configuration);
        var endpoint = configuration[EndpointKey]!;
        var containerName = configuration[ContainerKey] ?? "media";
        return builder.AddAzureBlobMediaFileSystem(options =>
            options.TryCreateBlobContainerClientUsingUri(_ => CreateContainerClient(endpoint, containerName, credential)));
    }
}

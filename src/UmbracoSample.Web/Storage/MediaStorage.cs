using Azure.Core;
using Azure.Storage.Blobs;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.StorageProviders.AzureBlob.IO;

namespace UmbracoSample.Web.Storage;

/// <summary>
/// Media lives in Blob storage when a blob endpoint is configured (Azure), and on local disk otherwise (development,
/// CI). The site reaches the container with its managed identity, never an account key or SAS token (ES-024); the
/// infrastructure also disables shared-key access on the account (ADR 0010, proposed in the infrastructure draft).
/// </summary>
public static class MediaStorage
{
    /// <summary>The blob service endpoint, such as <c>https://account.blob.core.windows.net/</c>. Never a connection string with a key.</summary>
    public const string EndpointKey = "Umbraco:Storage:AzureBlob:Media:ConnectionString";

    public const string ContainerKey = "Umbraco:Storage:AzureBlob:Media:ContainerName";

    public const string DefaultContainer = "media";

    public static bool IsConfigured(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return !string.IsNullOrWhiteSpace(configuration[EndpointKey]);
    }

    /// <summary>
    /// Refuses anything but a plain https service endpoint. Called at startup, before registration: the storage
    /// provider would otherwise fall back silently to a key-based client for a connection string it cannot parse as a URI.
    /// </summary>
    public static void ValidateEndpoint(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var service) || service.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(service.Query) || !string.IsNullOrEmpty(service.UserInfo))
        {
            throw new InvalidOperationException($"{EndpointKey} must be an https blob service endpoint without a key or SAS token.");
        }
    }

    /// <summary>Applies the site's rules to the provider's options: default container name, token credential only.</summary>
    public static void Configure(AzureBlobFileSystemOptions options, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credential);

        if (string.IsNullOrWhiteSpace(options.ContainerName))
        {
            options.ContainerName = DefaultContainer;
        }

        // The provider passes the container URI built from ConnectionString and ContainerName.
        options.TryCreateBlobContainerClientUsingUri(container => new BlobContainerClient(container, credential));
    }

    public static IUmbracoBuilder AddMediaStorage(this IUmbracoBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!IsConfigured(configuration))
        {
            return builder;
        }

        ValidateEndpoint(configuration[EndpointKey]);
        var credential = WorkloadIdentity.Credential(configuration);
        return builder.AddAzureBlobMediaFileSystem(options => Configure(options, credential));
    }
}

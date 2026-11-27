using Azure.Core;
using Microsoft.AspNetCore.DataProtection;

namespace UmbracoSample.Web.Storage;

/// <summary>
/// One ASP.NET Core Data Protection key ring for every replica and revision (ADR 0021; threat model 0001, R3). When both
/// settings are present the keys live in one blob and are wrapped by a Key Vault key, reached with the site's managed
/// identity (ES-024). Without them (development, CI) the framework's default per-machine store is used. Back-office
/// sign-in, antiforgery tokens and the lead form's render tokens all read through this ring.
/// </summary>
public static class KeyRing
{
    /// <summary>The blob that holds the key ring, such as <c>https://account.blob.core.windows.net/dataprotection/keys.xml</c>.</summary>
    public const string BlobUriKey = "DataProtection:KeyRingBlobUri";

    /// <summary>The Key Vault key that wraps the ring, without a version so a rotated key is used for new keys.</summary>
    public const string KeyUriKey = "DataProtection:KeyVaultKeyUri";

    /// <summary>
    /// Fixed, so every replica and revision reads the same keys whatever its content root. Changing it orphans the ring:
    /// editors are signed out and every open form's token stops reading.
    /// </summary>
    public const string ApplicationName = "umbraco-sample";

    public static bool IsConfigured(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return !string.IsNullOrWhiteSpace(configuration[BlobUriKey]) || !string.IsNullOrWhiteSpace(configuration[KeyUriKey]);
    }

    /// <summary>
    /// Refuses half a configuration, a key or SAS token in either URI, plain http, a blob URI without a container and
    /// blob name, and a URI that is not a Key Vault key. Called at startup, so a mistake stops the site instead of
    /// silently falling back to per-machine keys.
    /// </summary>
    public static (Uri Blob, Uri Key) Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var blob = Plain(configuration[BlobUriKey]);
        var key = Plain(configuration[KeyUriKey]);
        if (blob is null || blob.Segments.Length < 3)
        {
            throw new InvalidOperationException($"{BlobUriKey} must be an https blob URI (container and blob name) without a key or SAS token, set together with {KeyUriKey}.");
        }

        if (key is null || key.Segments.Length < 3 || !string.Equals(key.Segments[1], "keys/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{KeyUriKey} must be an https Key Vault key URI (https://vault/keys/name), set together with {BlobUriKey}.");
        }

        return (blob, key);
    }

    public static IServiceCollection AddKeyRing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        var dataProtection = services.AddDataProtection().SetApplicationName(ApplicationName);
        if (!IsConfigured(configuration))
        {
            return services;
        }

        var (blob, key) = Validate(configuration);
        TokenCredential credential = WorkloadIdentity.Credential(configuration);
        dataProtection.PersistKeysToAzureBlobStorage(blob, credential).ProtectKeysWithAzureKeyVault(key, credential);
        return services;
    }

    private static Uri? Plain(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.UserInfo)
            ? uri
            : null;
}

using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// The credential a deployed workload uses for Azure services: its managed identity, never a key or a developer
/// sign-in (ES-024). Infrastructure sets <c>AZURE_CLIENT_ID</c> to the user-assigned identity's client ID; without it
/// the system-assigned identity is used. One credential per identity is shared, so token caches are shared too.
/// </summary>
public static class WorkloadIdentity
{
    public const string ClientIdKey = "AZURE_CLIENT_ID";

    private const string SystemAssigned = "";

    private static readonly ConcurrentDictionary<string, TokenCredential> Credentials = new(StringComparer.Ordinal);

    public static TokenCredential Credential(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var clientId = configuration[ClientIdKey]?.Trim() ?? SystemAssigned;
        return Credentials.GetOrAdd(clientId, id => new ManagedIdentityCredential(id.Length == 0
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(id)));
    }
}

using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// The credential a deployed workload uses for Azure services: its managed identity, never a key or a developer
/// sign-in (ES-024, ADR 0010). Infrastructure sets <c>AZURE_CLIENT_ID</c> to the user-assigned identity's client ID.
/// </summary>
public static class WorkloadIdentity
{
    public const string ClientIdKey = "AZURE_CLIENT_ID";

    public static TokenCredential Credential(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var clientId = configuration[ClientIdKey];
        return new ManagedIdentityCredential(string.IsNullOrWhiteSpace(clientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(clientId));
    }
}

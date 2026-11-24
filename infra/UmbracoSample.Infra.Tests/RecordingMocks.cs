using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pulumi.Testing;

namespace UmbracoSample.Infra.Tests;

/// <summary>Pulumi mocks that record every declared resource's inputs, so tests assert on what would be sent to Azure without calling it.</summary>
internal sealed class RecordingMocks : IMocks
{
    public const string SubscriptionId = "00000000-0000-0000-0000-000000000001";
    public const string TenantId = "00000000-0000-0000-0000-000000000002";

    public ConcurrentBag<(string Type, string Name, ImmutableDictionary<string, object> Inputs)> Resources { get; } = [];

    public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        Resources.Add((args.Type!, args.Name!, args.Inputs));

        var state = args.Inputs.ToBuilder();
        state.TryAdd("name", args.Name!);
        state.TryAdd("principalId", $"{args.Name}-principal");
        state.TryAdd("clientId", $"{args.Name}-client");
        state.TryAdd("customerId", $"{args.Name}-customer");
        state.TryAdd("fullyQualifiedDomainName", $"{args.Name}.database.example");
        state.TryAdd("latestRevisionFqdn", $"{args.Name}.example");
        state.TryAdd("connectionString", "InstrumentationKey=mock");
        if (args.Type == "azure-native:storage:StorageAccount")
        {
            state["primaryEndpoints"] = ImmutableDictionary<string, object>.Empty.Add("blob", $"https://{args.Name}.blob.core.windows.net/");
        }

        if (args.Type == "azure-native:keyvault:Secret")
        {
            // Key Vault returns the secret's URI, never its value, as resource state.
            state["properties"] = ImmutableDictionary<string, object>.Empty.Add("secretUri", $"https://vault.example/secrets/{args.Name}");
        }

        return Task.FromResult<(string?, object)>(($"/mock/{args.Type}/{args.Name}", state.ToImmutable()));
    }

    public Task<object> CallAsync(MockCallArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        object result = args.Token switch
        {
            "azure-native:authorization:getClientConfig" => ImmutableDictionary<string, object>.Empty
                .Add("clientId", "mock-client")
                .Add("objectId", "mock-object")
                .Add("subscriptionId", SubscriptionId)
                .Add("tenantId", TenantId),
            "azure-native:operationalinsights:getSharedKeys" => ImmutableDictionary<string, object>.Empty
                .Add("primarySharedKey", "mock-shared-key"),
            _ => throw new InvalidOperationException($"Unexpected invoke {args.Token}."),
        };
        return Task.FromResult(result);
    }
}

namespace UmbracoSample.Web.Security;

/// <summary>
/// The only third-party origins that site pages may load anything from (TST-10, CMP-02). The content security policy
/// is built from this list, so the browser blocks anything else, and the integration tests fail on any rendered
/// reference the policy does not allow.
/// </summary>
/// <remarks>
/// Adding an origin is a reviewed code change, never configuration: an environment variable cannot add a tracker.
/// Each entry states what it is for and which kinds of resource it may serve. The list is empty today.
/// </remarks>
public static class ThirdPartyAllowlist
{
    public static IReadOnlyList<ThirdPartyOrigin> Origins { get; } = [];
}

/// <summary>An approved third-party origin, what it is for, and the resource kinds it may serve.</summary>
public sealed record ThirdPartyOrigin(string Origin, string Purpose, IReadOnlySet<ResourceKind> Kinds);

/// <summary>The kinds of resource a page loads, one per content security policy fetch directive.</summary>
public enum ResourceKind
{
    Script,
    Style,
    Image,
    Font,
    Connect,
    Frame,
}

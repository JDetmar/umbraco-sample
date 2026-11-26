using UmbracoSample.Security;

namespace UmbracoSample.Web.Security;

/// <summary>
/// The only third-party origins that this site's pages may load anything from (TST-10, CMP-02). The content security
/// policy is built from this list, so the browser blocks anything else, and the integration tests fail on any rendered
/// reference the policy does not allow.
/// </summary>
/// <remarks>
/// Adding an origin is a reviewed code change, never configuration: an environment variable cannot add a tracker.
/// Each entry states what it is for and which kinds of resource it may serve, and may name the hosts it applies to
/// (ADR 0020); with no hosts it applies to every host this site answers on. The list is empty today.
/// </remarks>
public static class ThirdPartyAllowlist
{
    public static IReadOnlyList<ThirdPartyOrigin> Origins { get; } = [];
}

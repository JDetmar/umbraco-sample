namespace UmbracoSample.Security;

/// <summary>
/// The one rule for a site host named in code or in a file name (ADR 0020), shared by the allowlist and the redirect maps
/// so they can never disagree.
/// </summary>
public static class SiteHost
{
    /// <summary>
    /// True for a host written as browsers send it in the Host header: lower-case ASCII, with at least one dot, no trailing
    /// dot, no port and no wildcard. An internationalised name is written in its punycode form (xn--), because a Unicode
    /// spelling would never match a request and the host's rules would silently not apply.
    /// </summary>
    public static bool IsValid(string? host) =>
        !string.IsNullOrEmpty(host)
        && host.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.')
        && host.Contains('.', StringComparison.Ordinal)
        && !host.EndsWith('.')
        && Uri.CheckHostName(host) == UriHostNameType.Dns;
}

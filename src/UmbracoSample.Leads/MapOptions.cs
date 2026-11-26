using System.ComponentModel.DataAnnotations;

namespace UmbracoSample.Leads;

/// <summary>
/// Where the marketing automation platform is and how long the site waits for it (threat model 0001, denial of service).
/// Bound from the <c>Leads:Map</c> configuration section. The API key is a secret: user secrets locally, Key Vault when
/// deployed, never in a committed file (ES-024).
/// </summary>
public sealed class MapOptions
{
    public const string Section = "Leads:Map";

    /// <summary>The MAP's base address. HTTPS, except a loopback stub in development and tests.</summary>
    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>A credential scoped to creating and looking up leads.</summary>
    [Required]
    public string? ApiKey { get; set; }

    /// <summary>The longest one create call may take.</summary>
    public TimeSpan CreateTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The whole budget for one submission, including the lookup and the retry.</summary>
    public TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The most calls to the MAP in flight at once from one replica (the bulkhead).</summary>
    [Range(1, 1000)]
    public int MaxConcurrentCalls { get; set; } = 20;

    /// <summary>The longest pause before the single retry; the actual pause is random below it.</summary>
    public TimeSpan MaxRetryJitter { get; set; } = TimeSpan.FromMilliseconds(500);

    internal string? Validate()
    {
        if (BaseAddress is not { IsAbsoluteUri: true } address)
        {
            return "Leads:Map:BaseAddress must be an absolute URI.";
        }

        if (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
        {
            return "Leads:Map:BaseAddress must use HTTPS (plain HTTP only to a loopback stub).";
        }

        if (CreateTimeout <= TimeSpan.Zero || Budget < CreateTimeout || MaxRetryJitter < TimeSpan.Zero)
        {
            return "Leads:Map timeouts must be positive, and the budget at least the create timeout.";
        }

        return null;
    }
}

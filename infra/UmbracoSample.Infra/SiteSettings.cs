using System.Globalization;
using System.Text.RegularExpressions;
using Pulumi;

namespace UmbracoSample.Infra;

/// <summary>Per-environment settings, parsed and validated before any resource is declared (ES-021).</summary>
public sealed partial record SiteSettings(
    string Environment,
    int MinReplicas,
    int MaxReplicas,
    decimal MonthlyBudget,
    string Image,
    bool EnableEdge)
{
    public static SiteSettings From(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var settings = new SiteSettings(
            Environment: config.Require("environment"),
            MinReplicas: int.Parse(config.Get("minReplicas") ?? "0", CultureInfo.InvariantCulture),
            MaxReplicas: int.Parse(config.Get("maxReplicas") ?? "1", CultureInfo.InvariantCulture),
            MonthlyBudget: decimal.Parse(config.Require("monthlyBudget"), CultureInfo.InvariantCulture),
            Image: config.Require("image"),
            EnableEdge: bool.Parse(config.Get("enableEdge") ?? "false"));

        settings.Validate();
        return settings;
    }

    /// <summary>Rejects settings that would break the hosting decisions in ADR 0004 and ADR 0010.</summary>
    public void Validate()
    {
        if (Environment is not ("dev" or "staging" or "prod"))
        {
            throw new ArgumentException($"environment must be dev, staging or prod, not '{Environment}'.");
        }

        if (MinReplicas < 0 || MaxReplicas < 1 || MinReplicas > MaxReplicas)
        {
            throw new ArgumentException("replicas must satisfy 0 <= minReplicas <= maxReplicas and maxReplicas >= 1.");
        }

        // Umbraco needs a shared data-protection key ring and explicit server roles before it can scale out (DEV-015).
        if (MaxReplicas > 1)
        {
            throw new ArgumentException("maxReplicas must be 1 until data protection keys are shared and server roles are set (DEV-015).");
        }

        if (Environment == "prod" && MinReplicas < 1)
        {
            throw new ArgumentException("prod keeps at least one warm replica; Umbraco cold starts are slow (ADR 0004).");
        }

        if (Environment == "prod" && !EnableEdge)
        {
            throw new ArgumentException("prod must sit behind the edge (Front Door with WAF, HSTS); see ADR 0010 and DEV-010.");
        }

        if (MonthlyBudget <= 0)
        {
            throw new ArgumentException("monthlyBudget must be positive (ES-047).");
        }



        if (Image.EndsWith("@sha256:" + new string('0', 64), StringComparison.Ordinal))
        {
            throw new ArgumentException("image is still the placeholder digest; set the digest CI published from main.");
        }

        // ES-049: releases are promoted by digest, never by a mutable tag.
        if (!ImageByDigest().IsMatch(Image))
        {
            throw new ArgumentException($"image must be pinned by sha256 digest, not '{Image}'.");
        }
    }

    [GeneratedRegex("^[a-z0-9.-]+(/[a-z0-9._-]+)+@sha256:[0-9a-f]{64}$")]
    private static partial Regex ImageByDigest();
}

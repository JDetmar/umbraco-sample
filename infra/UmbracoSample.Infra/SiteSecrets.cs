using Pulumi;

namespace UmbracoSample.Infra;

/// <summary>
/// Secrets the owner sets once per stack with <c>pulumi config set --secret</c>. They are encrypted in Pulumi Cloud
/// state, written to Key Vault, and reach the container only as Key Vault references (ES-024).
/// </summary>
public sealed record SiteSecrets(Output<string> AdminPassword, Output<string> ImagingHmacKey)
{
    public static SiteSecrets From(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new SiteSecrets(config.RequireSecret("umbracoAdminPassword"), config.RequireSecret("imagingHmacKey"));
    }
}

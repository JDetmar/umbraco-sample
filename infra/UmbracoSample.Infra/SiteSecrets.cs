using System.Net.Mail;
using Pulumi;

namespace UmbracoSample.Infra;

/// <summary>
/// Values the owner sets per stack with <c>pulumi config set --secret</c>, so the committed stack file holds only
/// ciphertext: credentials, and anything that identifies the owner or their tenant (this repository is public).
/// Credentials go to Key Vault and reach the container only as Key Vault references (ES-024).
/// </summary>
public sealed record SiteSecrets(
    Output<string> AdminPassword,
    Output<string> ImagingHmacKey,
    Output<string> AdminEmail,
    Output<string> BudgetContactEmail,
    Output<string> SqlAdminGroupName,
    Output<string> SqlAdminGroupObjectId)
{
    public static SiteSecrets From(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new SiteSecrets(
            config.RequireSecret("umbracoAdminPassword"),
            config.RequireSecret("imagingHmacKey"),
            config.RequireSecret("adminEmail").Apply(v => Email("adminEmail", v)),
            config.RequireSecret("budgetContactEmail").Apply(v => Email("budgetContactEmail", v)),
            config.RequireSecret("sqlAdminGroupName").Apply(v => NotBlank("sqlAdminGroupName", v)),
            config.RequireSecret("sqlAdminGroupObjectId").Apply(v => ObjectId("sqlAdminGroupObjectId", v)));
    }

    public static string Email(string key, string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value
            ? value
            : throw new ArgumentException($"{key} must be a plain email address.");

    public static string NotBlank(string key, string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{key} must not be blank.") : value;

    public static string ObjectId(string key, string value) =>
        Guid.TryParse(value, out _) ? value : throw new ArgumentException($"{key} must be an Entra object ID (a GUID).");
}

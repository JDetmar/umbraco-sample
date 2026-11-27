using System.ComponentModel.DataAnnotations;
using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Leads;

/// <summary>The lead form's numbers (threat model 0001, D3), bound from <c>Leads:Form</c>. The defaults are the decided ones.</summary>
public sealed class LeadFormOptions
{
    public const string Section = "Leads:Form";

    /// <summary>
    /// The largest request body the form endpoint reads (D3; raised from 16 KB by the owner on 2026-11-27). Every field at
    /// its limit at 9 URL-encoded bytes per counted character (the worst case) is about 22 KB, roughly 23 KB with the form's
    /// tokens, so 32 KB fits any language. A constant, because the controller's size attributes need one; the front-end body
    /// limit middleware is what enforces it.
    /// </summary>
    public const int MaxBodyBytes = 32 * 1024;

    [Range(1, 1000)]
    public int PermitLimit { get; set; } = 3;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(10);

    [Range(1, 10_000)]
    public int NameLimit { get; set; } = LeadLimits.Default.Name;

    [Range(6, LeadLimits.MaxEmail)]
    public int EmailLimit { get; set; } = LeadLimits.Default.Email;

    [Range(1, 10_000)]
    public int CompanyLimit { get; set; } = LeadLimits.Default.Company;

    [Range(1, 100_000)]
    public int MessageLimit { get; set; } = LeadLimits.Default.Message;

    public LeadLimits Limits => new(NameLimit, EmailLimit, CompanyLimit, MessageLimit);
}

/// <summary>
/// The CAPTCHA switch (threat model 0001, D1), bound from <c>Forms:Captcha</c> as the threat model names it. Off by default and everywhere until the
/// owner chooses a provider. On without a registered <see cref="ICaptchaVerifier"/>, the site refuses to start.
/// </summary>
public sealed class CaptchaOptions
{
    public const string Section = "Forms:Captcha";

    public bool Enabled { get; set; }
}

/// <summary>Verifies a CAPTCHA response server-side. No implementation exists until a provider is chosen (D1).</summary>
public interface ICaptchaVerifier
{
    Task<bool> VerifyAsync(string? response, CancellationToken cancellationToken);
}

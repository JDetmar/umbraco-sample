using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace UmbracoSample.Leads;

/// <summary>
/// What the signed render token proves (threat model 0001): which Form block rendered the form, when, and the idempotency
/// key for this rendering. The key is random per render, so the form page must never be cached (section 5).
/// </summary>
public sealed record RenderToken(Guid BlockKey, DateTimeOffset RenderedAt, string IdempotencyKey);

/// <summary>
/// Issues and reads render tokens, protected by ASP.NET Core Data Protection so a visitor can neither read nor change
/// them. A token from another purpose, another key ring, or with any byte changed does not read.
/// </summary>
/// <remarks>
/// Every replica must share the key ring before the form is enabled on a deployed environment (threat model 0001, R3);
/// otherwise a token signed on one replica does not read on another.
/// </remarks>
public sealed class RenderTokenProtector(IDataProtectionProvider dataProtection, TimeProvider time)
{
    private const string Purpose = "UmbracoSample.Leads.RenderToken.v1";
    private const int KeyBytes = 16;
    private const int PayloadBytes = 16 + 8 + KeyBytes;

    private readonly IDataProtector _protector = dataProtection.CreateProtector(Purpose);

    public string Issue(Guid blockKey)
    {
        Span<byte> payload = stackalloc byte[PayloadBytes];
        _ = blockKey.TryWriteBytes(payload[..16]); // always 16 bytes
        BinaryPrimitives.WriteInt64BigEndian(payload[16..24], time.GetUtcNow().ToUnixTimeMilliseconds());
        RandomNumberGenerator.Fill(payload[24..]);
        return WebEncoders.Base64UrlEncode(_protector.Protect(payload.ToArray()));
    }

    public bool TryRead(string? token, [NotNullWhen(true)] out RenderToken? renderToken)
    {
        renderToken = null;
        if (string.IsNullOrEmpty(token) || token.Length > 512)
        {
            return false;
        }

        byte[] payload;
        try
        {
            payload = _protector.Unprotect(WebEncoders.Base64UrlDecode(token));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }

        if (payload.Length != PayloadBytes)
        {
            return false;
        }

        var renderedAt = DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(16, 8)));
        renderToken = new RenderToken(new Guid(payload.AsSpan(0, 16)), renderedAt, WebEncoders.Base64UrlEncode(payload, 24, KeyBytes));
        return true;
    }
}

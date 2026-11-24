using System.Diagnostics.CodeAnalysis;

namespace UmbracoSample.Domain.Redirects;

/// <summary>
/// A normalised site-relative path, as matched by the redirect map: it starts with one slash, is lower case,
/// has no query, fragment, empty, "." or ".." segments, and has no trailing slash except for the root.
/// Text from the map is percent-decoded the way Kestrel decodes request paths (every escape except %2F), so a
/// source matches the request it describes and dot segments hidden in escapes are caught.
/// The only way to get one is <see cref="TryParse"/>, so every instance is valid (ES-021).
/// </summary>
public readonly record struct SitePath
{
    public const int MaxLength = 2048;

    private SitePath(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;

    /// <summary>Parses and normalises a path from the redirect map. Never throws; returns false with a reason for anything invalid.</summary>
    public static bool TryParse(string? text, out SitePath path, [NotNullWhen(false)] out string? error) =>
        TryParse(text, decode: true, out path, out error);

    /// <summary>Parses a request path that the server has already decoded, so it is not decoded again.</summary>
    public static bool TryParseDecoded(string? text, out SitePath path, [NotNullWhen(false)] out string? error) =>
        TryParse(text, decode: false, out path, out error);

    private static bool TryParse(string? text, bool decode, out SitePath path, [NotNullWhen(false)] out string? error)
    {
        path = default;
        if (string.IsNullOrEmpty(text))
        {
            error = "is empty";
            return false;
        }

        if (text.Length > MaxLength)
        {
            error = $"is longer than {MaxLength} characters";
            return false;
        }

        if (text[0] != '/' || text.StartsWith("//", StringComparison.Ordinal))
        {
            error = "must be a site-relative path starting with a single /";
            return false;
        }

        foreach (var c in text)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c) || c is '\\' or '?' or '#' or ',' or '"')
            {
                error = "must not contain whitespace, control characters, backslashes, commas, quotes, a query or a fragment";
                return false;
            }
        }

        if (decode)
        {
            if (!TryDecode(text, out var decoded))
            {
                error = "has an invalid percent-encoding";
                return false;
            }

            foreach (var c in decoded)
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c) || c is '\\' or '?' or '#' or ',' or '"')
                {
                    error = "must not encode whitespace, control characters, backslashes, commas, quotes, '?' or '#'";
                    return false;
                }
            }

            text = decoded;
        }

        var trimmed = text.Length > 1 ? text.TrimEnd('/') : text;
        if (trimmed.Length == 0)
        {
            trimmed = "/";
        }

        if (trimmed != "/")
        {
            foreach (var segment in trimmed[1..].Split('/'))
            {
                if (segment.Length == 0 || segment is "." or "..")
                {
                    error = "must not contain empty, '.' or '..' segments";
                    return false;
                }
            }
        }

        path = new SitePath(trimmed.ToLowerInvariant());
        error = null;
        return true;
    }

    /// <summary>Decodes %XX escapes as UTF-8, except %2F, which Kestrel also leaves encoded. Fails on malformed escapes.</summary>
    private static bool TryDecode(string text, out string decoded)
    {
        decoded = text;
        if (!text.Contains('%', StringComparison.Ordinal))
        {
            return true;
        }

        var bytes = new List<byte>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '%')
            {
                // Encode the whole run up to the next escape, so a surrogate pair (a character above U+FFFF) stays intact.
                var next = text.IndexOf('%', i);
                var end = next < 0 ? text.Length : next;
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(text[i..end]));
                i = end - 1;
                continue;
            }

            if (i + 2 >= text.Length || !Uri.IsHexDigit(text[i + 1]) || !Uri.IsHexDigit(text[i + 2]))
            {
                return false;
            }

            var value = (byte)((Uri.FromHex(text[i + 1]) << 4) | Uri.FromHex(text[i + 2]));
            if (value == (byte)'/')
            {
                bytes.AddRange("%2F"u8.ToArray());
            }
            else
            {
                bytes.Add(value);
            }

            i += 2;
        }

        try
        {
            decoded = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes.ToArray());
            return true;
        }
        catch (System.Text.DecoderFallbackException)
        {
            return false;
        }
    }
}

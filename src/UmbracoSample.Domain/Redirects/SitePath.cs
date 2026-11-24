using System.Diagnostics.CodeAnalysis;

namespace UmbracoSample.Domain.Redirects;

/// <summary>
/// A normalised site-relative path, as matched by the redirect map: it starts with one slash, is lower case,
/// has no query, fragment, empty, "." or ".." segments, and has no trailing slash except for the root.
/// The only way to get one is <see cref="TryParse"/>, so every instance is valid (ES-021).
/// </summary>
public readonly record struct SitePath
{
    public const int MaxLength = 2048;

    private SitePath(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;

    /// <summary>Parses and normalises a path. Never throws; returns false with a reason for anything invalid.</summary>
    public static bool TryParse(string? text, out SitePath path, [NotNullWhen(false)] out string? error)
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
}

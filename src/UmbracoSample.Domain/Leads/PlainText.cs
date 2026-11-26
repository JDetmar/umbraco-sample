using System.Globalization;
using System.Text;

namespace UmbracoSample.Domain.Leads;

/// <summary>
/// Normalises visitor text to plain text before it leaves the site (threat model 0001, field injection): control and
/// format characters (including bidirectional overrides and zero-width characters) are removed, and the result is
/// trimmed. A multi-line field keeps line breaks as "\n"; a single-line field turns them into spaces. The text is still
/// untrusted: it is never rendered unencoded.
/// </summary>
public static class PlainText
{
    public static string Normalise(string? text, bool multiline)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\r' or '\n')
            {
                // \r\n, \r and \n are one line break.
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                builder.Append(multiline ? '\n' : ' ');
                continue;
            }

            if (char.IsSurrogate(c))
            {
                // Keep only well-formed pairs; a lone surrogate is not text.
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    var category = CharUnicodeInfo.GetUnicodeCategory(text, i);
                    if (category is not (UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned))
                    {
                        builder.Append(c).Append(text[i + 1]);
                    }

                    i++;
                }

                continue;
            }

            if (char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.PrivateUse
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.OtherNotAssigned)
            {
                if (c == '\t' || char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                {
                    builder.Append(multiline && c != '\t' ? '\n' : ' ');
                }

                continue;
            }

            builder.Append(c);
        }

        return builder.ToString().Trim();
    }
}

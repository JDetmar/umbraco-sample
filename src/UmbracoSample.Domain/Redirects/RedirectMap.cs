using System.Collections.Frozen;
using System.Globalization;

namespace UmbracoSample.Domain.Redirects;

/// <summary>Permanent (301) or temporary (302).</summary>
public enum RedirectKind
{
    Permanent = 301,
    Temporary = 302,
}

public sealed record RedirectRule(SitePath Source, RedirectTarget Target, RedirectKind Kind, int Line);

public sealed record RedirectError(int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"line {Line}: {Message}" : Message;
}

/// <summary>
/// The redirect map, parsed from CSV (<c>source,target,kind</c>, with a header row; '#' starts a comment line).
/// A parsed map is flat: no source is also a target of another rule, so there are no chains and no loops,
/// and every visitor reaches the final page in one redirect.
/// </summary>
public sealed class RedirectMap
{
    public const string Header = "source,target,kind";

    private readonly FrozenDictionary<string, RedirectRule> _bySource;

    private RedirectMap(IReadOnlyList<RedirectRule> rules)
    {
        Rules = rules;
        _bySource = rules.ToFrozenDictionary(rule => rule.Source.Value, StringComparer.Ordinal);
    }

    public static RedirectMap Empty { get; } = new([]);

    public IReadOnlyList<RedirectRule> Rules { get; }

    /// <summary>Finds the rule for a request path as the server decoded it, normalised like sources. Returns null if none applies.</summary>
    public RedirectRule? Find(string? requestPath) =>
        SitePath.TryParseDecoded(requestPath, out var path, out _) && _bySource.TryGetValue(path.Value, out var rule) ? rule : null;

    /// <summary>
    /// Parses the map. Never throws: every problem is returned with its line number, and the map is null if there are any.
    /// </summary>
    /// <param name="text">The CSV text.</param>
    /// <param name="reservedPrefixes">Paths that redirects must never capture, such as the back office or health endpoints.</param>
    public static (RedirectMap? Map, IReadOnlyList<RedirectError> Errors) Parse(string? text, IEnumerable<string>? reservedPrefixes = null)
    {
        var errors = new List<RedirectError>();
        var rules = new List<RedirectRule>();
        var reserved = (reservedPrefixes ?? []).Select(prefix => prefix.TrimEnd('/').ToLowerInvariant()).ToList();
        // A byte order mark from a spreadsheet export is not part of the header.
        var lines = (text ?? string.Empty).TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var sawHeader = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!sawHeader)
            {
                sawHeader = true;
                if (!string.Equals(line, Header, StringComparison.Ordinal))
                {
                    errors.Add(new RedirectError(lineNumber, $"the first row must be the header '{Header}'"));
                }

                continue;
            }

            var fields = line.Split(',');
            if (fields.Length != 3)
            {
                errors.Add(new RedirectError(lineNumber, "expected three fields: source,target,kind"));
                continue;
            }

            var ok = true;
            if (!SitePath.TryParse(fields[0], out var source, out var sourceError))
            {
                errors.Add(new RedirectError(lineNumber, $"source {sourceError}"));
                ok = false;
            }
            else if (source.Value == "/" || reserved.Any(prefix => source.Value == prefix || source.Value.StartsWith(prefix + "/", StringComparison.Ordinal)))
            {
                errors.Add(new RedirectError(lineNumber, $"source {source} is the home page or a reserved path"));
                ok = false;
            }

            if (!RedirectTarget.TryParse(fields[1], out var target, out var targetError))
            {
                errors.Add(new RedirectError(lineNumber, $"target {targetError}"));
                ok = false;
            }

            RedirectKind kind = default;
            switch (fields[2])
            {
                case "permanent":
                    kind = RedirectKind.Permanent;
                    break;
                case "temporary":
                    kind = RedirectKind.Temporary;
                    break;
                default:
                    errors.Add(new RedirectError(lineNumber, "kind must be 'permanent' or 'temporary'"));
                    ok = false;
                    break;
            }

            if (ok)
            {
                rules.Add(new RedirectRule(source, target!, kind, lineNumber));
            }
        }

        if (!sawHeader)
        {
            errors.Add(new RedirectError(0, $"the map has no header row '{Header}'"));
        }

        errors.AddRange(CrossRuleErrors(rules));
        return errors.Count == 0 ? (new RedirectMap(rules), errors) : (null, errors);
    }

    private static List<RedirectError> CrossRuleErrors(List<RedirectRule> rules)
    {
        // A plain method rather than an iterator, so mutation testing can mutate it (Stryker cannot compile some mutants of iterators).
        var errors = new List<RedirectError>();
        var firstLineBySource = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (!firstLineBySource.TryAdd(rule.Source.Value, rule.Line))
            {
                errors.Add(new RedirectError(rule.Line, string.Create(CultureInfo.InvariantCulture, $"source {rule.Source} is already mapped on line {firstLineBySource[rule.Source.Value]}")));
            }
        }

        foreach (var rule in rules)
        {
            if (!rule.Target.SitePath.HasValue)
            {
                continue;
            }

            var targetPath = rule.Target.SitePath.Value;

            if (targetPath == rule.Source)
            {
                errors.Add(new RedirectError(rule.Line, $"source {rule.Source} redirects to itself"));
            }
            else if (firstLineBySource.TryGetValue(targetPath.Value, out var chained))
            {
                errors.Add(new RedirectError(rule.Line, string.Create(CultureInfo.InvariantCulture, $"target {targetPath} is itself redirected on line {chained}; point this rule at the final page instead")));
            }
        }

        return errors;
    }
}

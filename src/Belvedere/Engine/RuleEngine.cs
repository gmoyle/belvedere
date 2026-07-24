using System.Text.RegularExpressions;
using Belvedere.Models;

namespace Belvedere.Engine;

/// <summary>
/// Pure evaluation logic: does a given file satisfy a rule's conditions?
/// Faithful port of the original subject/verb semantics (case-insensitive
/// text, size in the chosen unit, "in the last" as elapsed-since).
/// </summary>
public static class RuleEngine
{
    public static bool Matches(Rule rule, FileInfo file)
    {
        if (rule.Conditions.Count == 0)
            return false;

        foreach (var c in rule.Conditions)
        {
            bool ok = Evaluate(c, file);

            if (rule.Match == MatchMode.All && !ok)
                return false;
            if (rule.Match == MatchMode.Any && ok)
                return true;
        }

        // ALL: fell through with no failure => true. ANY: no match found => false.
        return rule.Match == MatchMode.All;
    }

    private static bool Evaluate(Condition c, FileInfo file)
    {
        return c.Subject switch
        {
            Subject.Name => EvalText(c, GetName(file)),
            Subject.Extension => EvalText(c, GetExtension(file)),
            Subject.Size => EvalSize(c, file),
            Subject.DateModified => EvalDate(c, file.LastWriteTime),
            Subject.DateAccessed => EvalDate(c, file.LastAccessTime),
            Subject.DateCreated => EvalDate(c, file.CreationTime),
            _ => false,
        };
    }

    private static string GetName(FileInfo f) => Path.GetFileNameWithoutExtension(f.Name);

    private static string GetExtension(FileInfo f) =>
        f.Extension.TrimStart('.'); // "jpg", not ".jpg", matching the original

    // -- Text verbs (case-insensitive) --------------------------------------

    private static bool EvalText(Condition c, string subject)
    {
        const StringComparison ci = StringComparison.OrdinalIgnoreCase;
        string value = c.Value ?? string.Empty;

        return c.Verb switch
        {
            Verb.Is => string.Equals(subject, value, ci),
            Verb.IsNot => !string.Equals(subject, value, ci),
            Verb.Contains => subject.Contains(value, ci),
            Verb.DoesNotContain => !subject.Contains(value, ci),
            Verb.MatchesOneOf => SplitList(value).Any(v => string.Equals(subject, v, ci)),
            Verb.DoesNotMatchOneOf => !SplitList(value).Any(v => string.Equals(subject, v, ci)),
            Verb.ContainsOneOf => SplitList(value).Any(v => v.Length > 0 && subject.Contains(v, ci)),
            Verb.Regex => SafeRegex(subject, value),
            _ => false,
        };
    }

    private static IEnumerable<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool SafeRegex(string subject, string pattern)
    {
        try { return Regex.IsMatch(subject, pattern, RegexOptions.IgnoreCase); }
        catch (ArgumentException) { return false; } // invalid pattern never matches
    }

    // -- Size verbs ----------------------------------------------------------

    private static bool EvalSize(Condition c, FileInfo file)
    {
        if (!double.TryParse(c.Value, out double target))
            return false;

        double divisor = c.SizeUnit switch
        {
            SizeUnit.KB => 1024d,
            SizeUnit.MB => 1024d * 1024d,
            SizeUnit.GB => 1024d * 1024d * 1024d,
            _ => 1d,
        };
        double sizeInUnit = file.Length / divisor;

        return c.Verb switch
        {
            Verb.Is => Math.Abs(sizeInUnit - target) < 0.001,
            Verb.IsNot => Math.Abs(sizeInUnit - target) >= 0.001,
            Verb.IsGreaterThan => sizeInUnit > target,
            Verb.IsGreaterThanOrEqual => sizeInUnit >= target,
            Verb.IsLessThan => sizeInUnit < target,
            Verb.IsLessThanOrEqual => sizeInUnit <= target,
            _ => false,
        };
    }

    // -- Date verbs ("in the last N units") ---------------------------------

    private static bool EvalDate(Condition c, DateTime fileTime)
    {
        if (!double.TryParse(c.Value, out double amount))
            return false;

        double elapsedSeconds = (DateTime.Now - fileTime).TotalSeconds;
        double windowSeconds = amount * UnitToSeconds(c.TimeUnit);

        bool inTheLast = elapsedSeconds < windowSeconds;

        return c.Verb switch
        {
            Verb.IsInTheLast => inTheLast,
            Verb.IsNotInTheLast => !inTheLast,
            _ => false,
        };
    }

    public static double UnitToSeconds(TimeUnit unit) => unit switch
    {
        TimeUnit.Seconds => 1,
        TimeUnit.Minutes => 60,
        TimeUnit.Hours => 3600,
        TimeUnit.Days => 86400,
        TimeUnit.Weeks => 604800,
        _ => 1,
    };
}

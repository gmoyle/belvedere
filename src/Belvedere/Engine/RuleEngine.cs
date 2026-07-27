using System.Text.RegularExpressions;
using Belvedere.Models;

namespace Belvedere.Engine;

/// <summary>
/// Pure evaluation logic: does a given file or folder satisfy a rule's
/// conditions? Faithful port of the original subject/verb semantics
/// (case-insensitive text, size in the chosen unit, "in the last" as
/// elapsed-since). Works against <see cref="FileSystemInfo"/> so the same
/// logic serves both file-targeting and folder-targeting rules.
/// </summary>
public static class RuleEngine
{
    /// <summary>The single source of truth for "would this rule act on this
    /// entry?" - attribute skip plus condition matching. Used both by the real
    /// sweep (<see cref="RuleProcessor"/>) and the dry-run preview, so the two
    /// can never disagree about what a rule would do.</summary>
    public static bool ShouldProcess(Rule rule, FileSystemInfo entry) =>
        !IsSkippedByAttribute(rule, entry) && Matches(rule, entry);

    public static bool IsSkippedByAttribute(Rule rule, FileSystemInfo entry)
    {
        var a = entry.Attributes;
        if (rule.SkipReadOnly && a.HasFlag(FileAttributes.ReadOnly)) return true;
        if (rule.SkipHidden && a.HasFlag(FileAttributes.Hidden)) return true;
        if (rule.SkipSystem && a.HasFlag(FileAttributes.System)) return true;
        return false;
    }

    public static bool Matches(Rule rule, FileSystemInfo entry)
    {
        if (rule.Conditions.Count == 0)
            return false;

        foreach (var c in rule.Conditions)
        {
            bool ok = Evaluate(c, entry);

            if (rule.Match == MatchMode.All && !ok)
                return false;
            if (rule.Match == MatchMode.Any && ok)
                return true;
        }

        // ALL: fell through with no failure => true. ANY: no match found => false.
        return rule.Match == MatchMode.All;
    }

    private static bool Evaluate(Condition c, FileSystemInfo entry)
    {
        return c.Subject switch
        {
            Subject.Name => EvalText(c, GetName(entry)),
            Subject.Extension => EvalText(c, GetExtension(entry)),
            // Folders have no meaningful size in this app (would require an
            // expensive recursive sum); a Size condition on a folder never matches.
            Subject.Size => entry is FileInfo file && EvalSize(c, file),
            Subject.DateModified => EvalDate(c, entry.LastWriteTime),
            Subject.DateAccessed => EvalDate(c, entry.LastAccessTime),
            Subject.DateCreated => EvalDate(c, entry.CreationTime),
            _ => false,
        };
    }

    private static string GetName(FileSystemInfo f) => Path.GetFileNameWithoutExtension(f.Name);

    private static string GetExtension(FileSystemInfo f) =>
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

    // -- Size verbs (files only) ---------------------------------------------

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

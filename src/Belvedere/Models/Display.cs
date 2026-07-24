namespace Belvedere.Models;

/// <summary>
/// Friendly labels for the enums, matching the wording the original Belvedere
/// used in its UI and INI file. Keeping the exact strings makes the rules.ini
/// importer and the editor drop-downs trivial and familiar.
/// </summary>
public static class Display
{
    public static string Label(this Subject s) => s switch
    {
        Subject.Name => "Name",
        Subject.Extension => "Extension",
        Subject.Size => "Size",
        Subject.DateModified => "Date last modified",
        Subject.DateAccessed => "Date last opened",
        Subject.DateCreated => "Date created",
        _ => s.ToString(),
    };

    public static string Label(this Verb v) => v switch
    {
        Verb.Is => "is",
        Verb.IsNot => "is not",
        Verb.MatchesOneOf => "matches one of",
        Verb.DoesNotMatchOneOf => "does not match one of",
        Verb.Contains => "contains",
        Verb.DoesNotContain => "does not contain",
        Verb.ContainsOneOf => "contains one of",
        Verb.Regex => "RegEx",
        Verb.IsGreaterThan => "is greater than",
        Verb.IsGreaterThanOrEqual => "is greater than or equal",
        Verb.IsLessThan => "is less than",
        Verb.IsLessThanOrEqual => "is less than or equal",
        Verb.IsInTheLast => "is in the last",
        Verb.IsNotInTheLast => "is not in the last",
        _ => v.ToString(),
    };

    public static string Label(this ActionType a) => a switch
    {
        ActionType.Move => "Move file",
        ActionType.MoveLeaveShortcut => "Move file & leave shortcut",
        ActionType.Rename => "Rename file",
        ActionType.Recycle => "Send file to Recycle Bin",
        ActionType.Delete => "Delete file",
        ActionType.Copy => "Copy file",
        ActionType.Open => "Open file",
        ActionType.Print => "Print file",
        ActionType.Custom => "Custom command",
        _ => a.ToString(),
    };

    /// <summary>Which verbs are valid for a given subject (drives the editor).</summary>
    public static Verb[] VerbsFor(Subject s) => s switch
    {
        Subject.Size => new[]
        {
            Verb.Is, Verb.IsNot,
            Verb.IsGreaterThan, Verb.IsGreaterThanOrEqual,
            Verb.IsLessThan, Verb.IsLessThanOrEqual,
        },
        Subject.DateModified or Subject.DateAccessed or Subject.DateCreated => new[]
        {
            Verb.IsInTheLast, Verb.IsNotInTheLast,
        },
        _ => new[]
        {
            Verb.Is, Verb.IsNot, Verb.MatchesOneOf, Verb.DoesNotMatchOneOf,
            Verb.Contains, Verb.DoesNotContain, Verb.ContainsOneOf, Verb.Regex,
        },
    };

    public static bool IsDateSubject(this Subject s) =>
        s is Subject.DateModified or Subject.DateAccessed or Subject.DateCreated;

    // -- Reverse lookups (label/legacy string -> enum), for the INI importer --

    public static Subject? ParseSubject(string s) => s.Trim().ToLowerInvariant() switch
    {
        "name" => Subject.Name,
        "extension" => Subject.Extension,
        "size" => Subject.Size,
        "date last modified" => Subject.DateModified,
        "date last opened" => Subject.DateAccessed,
        "date created" => Subject.DateCreated,
        _ => null,
    };

    public static Verb? ParseVerb(string s)
    {
        foreach (Verb v in Enum.GetValues<Verb>())
            if (string.Equals(v.Label(), s.Trim(), StringComparison.OrdinalIgnoreCase))
                return v;
        return null;
    }

    public static ActionType? ParseAction(string s)
    {
        string t = s.Trim();
        foreach (ActionType a in Enum.GetValues<ActionType>())
            if (string.Equals(a.Label(), t, StringComparison.OrdinalIgnoreCase))
                return a;
        // Legacy label for the custom action.
        if (string.Equals(t, "Custom", StringComparison.OrdinalIgnoreCase)) return ActionType.Custom;
        return null;
    }

    public static SizeUnit ParseSizeUnit(string s) => s.Trim().ToUpperInvariant() switch
    {
        "KB" => SizeUnit.KB,
        "GB" => SizeUnit.GB,
        _ => SizeUnit.MB,
    };

    public static TimeUnit ParseTimeUnit(string s) => s.Trim().ToLowerInvariant() switch
    {
        "seconds" => TimeUnit.Seconds,
        "minutes" => TimeUnit.Minutes,
        "hours" => TimeUnit.Hours,
        "days" => TimeUnit.Days,
        "weeks" => TimeUnit.Weeks,
        _ => TimeUnit.Days,
    };
}

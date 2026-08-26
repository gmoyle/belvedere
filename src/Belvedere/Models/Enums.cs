namespace Belvedere.Models;

/// <summary>The file attribute a condition inspects.</summary>
public enum Subject
{
    Name,
    Extension,
    Size,
    DateModified,
    DateAccessed,
    DateCreated,

    /// <summary>The most recent of "last edited" and "last opened by the user"
    /// (the latter read from Windows Recent Items history). Unlike DateAccessed,
    /// this is a reliable "when did I last touch this?" signal on a cloud-synced
    /// machine - it catches files you open to look at but never modify.</summary>
    DateUsed,
}

/// <summary>The comparison operator a condition applies.</summary>
public enum Verb
{
    // Text verbs
    Is,
    IsNot,
    MatchesOneOf,
    DoesNotMatchOneOf,
    Contains,
    DoesNotContain,
    ContainsOneOf,
    Regex,

    // Numeric verbs (Size)
    IsGreaterThan,
    IsGreaterThanOrEqual,
    IsLessThan,
    IsLessThanOrEqual,

    // Date verbs
    IsInTheLast,
    IsNotInTheLast,
}

/// <summary>What happens to a file that matches a rule.</summary>
public enum ActionType
{
    Move,
    MoveLeaveShortcut,
    Rename,
    Recycle,
    Delete,
    Copy,
    Open,
    Print,
    Custom,
}

/// <summary>How multiple conditions in a rule combine.</summary>
public enum MatchMode
{
    All,
    Any,
}

/// <summary>What kind of filesystem entry a rule matches and acts on.
/// Files is the default and preserves all pre-existing behavior; Folders lets
/// a rule match and act on subfolders themselves rather than the files inside
/// them.</summary>
public enum MatchTarget
{
    Files,
    Folders,
}

/// <summary>Unit for size comparisons.</summary>
public enum SizeUnit
{
    KB,
    MB,
    GB,
}

/// <summary>Unit for "in the last" date comparisons.</summary>
public enum TimeUnit
{
    Seconds,
    Minutes,
    Hours,
    Days,
    Weeks,
}

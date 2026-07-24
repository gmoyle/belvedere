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

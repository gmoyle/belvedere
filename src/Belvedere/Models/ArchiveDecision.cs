namespace Belvedere.Models;

/// <summary>
/// A standing decision made during an archive review - the persisted record of a
/// "Keep" or "Snooze" click. Decisions are what a managed folder's rules are made
/// of: instead of authoring exclusions up front, the user accumulates them one
/// click at a time as proposals come up. The scanner consults them to suppress
/// future proposals.
/// </summary>
public sealed class ArchiveDecision
{
    public DecisionKind Kind { get; set; } = DecisionKind.KeepForever;

    public DecisionScope Scope { get; set; } = DecisionScope.Path;

    /// <summary>The thing matched: an absolute path (Path/Folder), a bare
    /// extension without the dot (Extension), or a filename glob (Glob).</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>When a <see cref="DecisionKind.SnoozeUntil"/> decision expires.
    /// Null for <see cref="DecisionKind.KeepForever"/>.</summary>
    public DateTime? SnoozeUntil { get; set; }

    public DateTime Created { get; set; } = DateTime.Now;
}

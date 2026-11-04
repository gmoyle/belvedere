namespace Belvedere.Models;

/// <summary>
/// A folder put under management for review-first archiving. The user configures
/// one age + one destination; Belvedere proposes stale files/folders for the user
/// to approve (Archive / Keep / Snooze) rather than acting silently. This is the
/// simple, primary alternative to the condition-based <see cref="Rule"/> system,
/// which remains for advanced use.
/// </summary>
public sealed class ManagedFolder
{
    public bool Enabled { get; set; } = true;

    /// <summary>The folder whose contents are reviewed for archiving.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Where archived items go, preserving their subfolder structure.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>How long a thing may sit unused before it's proposed for archiving.</summary>
    public int MaxIdleAge { get; set; } = 8;
    public TimeUnit MaxIdleUnit { get; set; } = TimeUnit.Weeks;

    /// <summary>Which timestamp counts as "last touched".</summary>
    public IdleBasis Basis { get; set; } = IdleBasis.Used;

    /// <summary>Standing Keep/Snooze decisions accumulated from past reviews. These
    /// are this folder's rules, authored by clicking rather than up front.</summary>
    public List<ArchiveDecision> Decisions { get; set; } = new();
}

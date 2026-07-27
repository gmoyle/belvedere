namespace Belvedere.Models;

/// <summary>
/// A named rule: watch a source folder, and for every file that satisfies the
/// conditions, perform an action. This flattens the original design (where
/// folders and rules were cross-referenced by name in the INI) into a single
/// self-contained object.
/// </summary>
public sealed class Rule
{
    public string Name { get; set; } = "New rule";
    public bool Enabled { get; set; } = true;

    /// <summary>Folder whose contents are evaluated.</summary>
    public string SourceFolder { get; set; } = string.Empty;

    /// <summary>Recurse into subfolders.</summary>
    public bool Recursive { get; set; }

    /// <summary>Whether this rule matches files or subfolders themselves.
    /// Defaults to Files, so every pre-existing rule/import is unaffected.</summary>
    public MatchTarget Target { get; set; } = MatchTarget.Files;

    /// <summary>Whether ALL conditions or ANY condition must match.</summary>
    public MatchMode Match { get; set; } = MatchMode.All;

    public List<Condition> Conditions { get; set; } = new();

    public ActionType Action { get; set; } = ActionType.Move;

    /// <summary>Destination folder (Move/Copy), rename template (Rename), or
    /// executable/command (Custom). Unused for Recycle/Delete/Open/Print.</summary>
    public string Destination { get; set; } = string.Empty;

    public bool Overwrite { get; set; }

    /// <summary>Ask before acting on each matched file.</summary>
    public bool ConfirmAction { get; set; }

    // Skip files carrying these attributes (matches original behavior).
    public bool SkipReadOnly { get; set; }
    public bool SkipHidden { get; set; }
    public bool SkipSystem { get; set; }
}

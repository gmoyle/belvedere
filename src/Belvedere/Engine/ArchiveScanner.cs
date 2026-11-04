using System.Text.RegularExpressions;
using Belvedere.Models;
using Belvedere.Services;

namespace Belvedere.Engine;

/// <summary>Whether a proposal is a single file or a whole subfolder.</summary>
public enum ProposalKind { File, Folder }

/// <summary>One thing the scanner proposes archiving - a file, or (when a whole
/// subfolder has gone stale) the folder itself. Preview-only: nothing is moved
/// until the user approves it in review.</summary>
public sealed record ArchiveProposal(
    ProposalKind Kind,
    FileSystemInfo Entry,
    string RelativePath,
    DateTime LastUsed,
    long SizeBytes);

/// <summary>A review group: either the managed folder's loose top-level files, or
/// one immediate subfolder. <see cref="AllStale"/> means every file inside the
/// subfolder is stale, so the whole folder can be archived as a unit (structure
/// intact) instead of file by file.</summary>
public sealed record ProposalGroup(
    string Label,
    string? SubfolderPath,
    bool AllStale,
    IReadOnlyList<ArchiveProposal> Proposals);

/// <summary>The result of scanning one managed folder: what it proposes archiving,
/// grouped for review.</summary>
public sealed record ArchivePlan(
    ManagedFolder Folder,
    IReadOnlyList<ProposalGroup> Groups,
    int EntriesScanned,
    bool Truncated)
{
    public int TotalProposals => Groups.Sum(g => g.Proposals.Count);
    public bool IsEmpty => Groups.Count == 0;

    public static ArchivePlan Empty(ManagedFolder folder) =>
        new(folder, Array.Empty<ProposalGroup>(), 0, false);
}

/// <summary>
/// Read-only scanner for the review-first archiving model. Given a managed folder,
/// it finds everything stale enough to propose for archiving and groups it for the
/// review window - loose top-level files, plus one group per immediate subfolder
/// (offering the whole folder when all of it is stale). It consults the folder's
/// standing Keep/Snooze decisions to suppress things the user has already ruled
/// out. It never touches the filesystem.
/// </summary>
public static class ArchiveScanner
{
    // Mirror DryRunPreview's guardrails so pointing this at something huge can't
    // hang the UI or build an unbounded list.
    private const int MaxEntriesToScan = 50_000;

    public static ArchivePlan Scan(ManagedFolder folder, IReadOnlyDictionary<string, DateTime>? opens = null)
    {
        if (!folder.Enabled || string.IsNullOrWhiteSpace(folder.Path) || !Directory.Exists(folder.Path))
            return ArchivePlan.Empty(folder);

        string root = folder.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Only "Date last used" needs the Windows open-history, and reading it
        // isn't free - fetch it once here if the caller didn't supply it.
        opens ??= folder.Basis == IdleBasis.Used ? UsageHistory.Snapshot() : null;

        double windowSeconds = folder.MaxIdleAge * RuleEngine.UnitToSeconds(folder.MaxIdleUnit);
        DateTime now = DateTime.Now;

        var groups = new List<ProposalGroup>();
        int scanned = 0;
        bool truncated = false;

        bool IsStale(FileInfo f)
        {
            DateTime basis = folder.Basis == IdleBasis.Used ? RuleEngine.LatestUse(f, opens) : f.LastWriteTime;
            return (now - basis).TotalSeconds >= windowSeconds;
        }

        // -- Loose top-level files --------------------------------------------
        try
        {
            var loose = new List<ArchiveProposal>();
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                if (++scanned > MaxEntriesToScan) { truncated = true; break; }
                var file = SafeFile(path);
                if (file is null) continue;
                if (IsSuppressed(folder, file, now)) continue;
                if (!IsStale(file)) continue;
                loose.Add(FileProposal(root, file));
            }
            if (loose.Count > 0)
                groups.Add(new ProposalGroup("Loose files", null, AllStale: false, loose));
        }
        catch { /* unreadable root: fall through with whatever we have */ }

        // -- One group per immediate subfolder --------------------------------
        if (!truncated)
        {
            IEnumerable<string> subdirs;
            try { subdirs = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly); }
            catch { subdirs = Enumerable.Empty<string>(); }

            foreach (var subdir in subdirs)
            {
                var dir = SafeDir(subdir);
                if (dir is null) continue;

                var fileProposals = new List<ArchiveProposal>();
                int filesInFolder = 0;
                bool anyKept = false;

                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(subdir, "*", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (var path in files)
                {
                    if (++scanned > MaxEntriesToScan) { truncated = true; break; }
                    var file = SafeFile(path);
                    if (file is null) continue;
                    filesInFolder++;

                    if (IsSuppressed(folder, file, now)) { anyKept = true; continue; }
                    if (IsStale(file)) fileProposals.Add(FileProposal(root, file));
                }

                if (fileProposals.Count == 0) { if (truncated) break; else continue; }

                // "All stale" = every file present is being proposed, nothing was
                // kept/snoozed, and the subfolder itself isn't under a Keep. Only
                // then is moving the whole folder as a unit safe.
                bool allStale = !anyKept
                    && fileProposals.Count == filesInFolder
                    && !IsSuppressed(folder, dir, now);

                string rel = Path.GetRelativePath(root, dir.FullName);
                groups.Add(new ProposalGroup(rel, dir.FullName, allStale, fileProposals));

                if (truncated) break;
            }
        }

        return new ArchivePlan(folder, groups, scanned, truncated);
    }

    private static ArchiveProposal FileProposal(string root, FileInfo file) =>
        new(ProposalKind.File, file, Path.GetRelativePath(root, file.FullName),
            RuleEngine.LatestUse(file, null), file.Length);

    /// <summary>Builds the single "move this whole subfolder" proposal for an
    /// all-stale group (used when the user chooses "Archive whole folder" instead
    /// of deciding file by file). Returns null if the group isn't a subfolder.</summary>
    public static ArchiveProposal? FolderProposalFor(ManagedFolder folder, ProposalGroup group)
    {
        if (group.SubfolderPath is null) return null;
        string root = folder.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = new DirectoryInfo(group.SubfolderPath);
        DateTime latest = group.Proposals.Count > 0 ? group.Proposals.Max(p => p.LastUsed) : dir.LastWriteTime;
        long size = group.Proposals.Sum(p => p.SizeBytes);
        return new ArchiveProposal(ProposalKind.Folder, dir,
            Path.GetRelativePath(root, dir.FullName), latest, size);
    }

    private static FileInfo? SafeFile(string path)
    {
        try { var f = new FileInfo(path); return f.Exists ? f : null; } catch { return null; }
    }

    private static DirectoryInfo? SafeDir(string path)
    {
        try { var d = new DirectoryInfo(path); return d.Exists ? d : null; } catch { return null; }
    }

    // -- Decision matching ----------------------------------------------------

    /// <summary>True if a standing Keep/Snooze decision currently covers this
    /// entry, so it should not be proposed. Expired snoozes don't count.</summary>
    private static bool IsSuppressed(ManagedFolder folder, FileSystemInfo entry, DateTime now)
    {
        foreach (var d in folder.Decisions)
        {
            if (d.Kind == DecisionKind.SnoozeUntil && (d.SnoozeUntil is null || d.SnoozeUntil <= now))
                continue; // snooze lapsed - no longer suppresses

            if (Covers(d, entry)) return true;
        }
        return false;
    }

    private static bool Covers(ArchiveDecision d, FileSystemInfo entry)
    {
        const StringComparison ci = StringComparison.OrdinalIgnoreCase;
        return d.Scope switch
        {
            DecisionScope.Path => string.Equals(entry.FullName, d.Target, ci),
            DecisionScope.Folder => IsWithin(entry.FullName, d.Target),
            DecisionScope.Extension => entry is FileInfo f
                && string.Equals(f.Extension.TrimStart('.'), d.Target.TrimStart('.'), ci),
            DecisionScope.Glob => GlobMatch(d.Target, entry.Name),
            _ => false,
        };
    }

    /// <summary>True if <paramref name="path"/> is the folder <paramref name="folder"/>
    /// itself or anything inside it.</summary>
    private static bool IsWithin(string path, string folder)
    {
        const StringComparison ci = StringComparison.OrdinalIgnoreCase;
        string f = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(path, f, ci)) return true;
        return path.StartsWith(f + Path.DirectorySeparatorChar, ci);
    }

    private static bool GlobMatch(string glob, string name)
    {
        if (string.IsNullOrEmpty(glob)) return false;
        string pattern = "^" + Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        try { return Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase); }
        catch (ArgumentException) { return false; }
    }
}

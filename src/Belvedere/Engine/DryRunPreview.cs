using Belvedere.Models;

namespace Belvedere.Engine;

/// <summary>A single file or folder a rule would currently act on, and a
/// description of what that action would be. Preview-only - no filesystem
/// changes.</summary>
public sealed record PreviewMatch(FileSystemInfo Entry, string ActionDescription);

/// <summary>The outcome of a dry run: the matches found (capped for display),
/// how many matched in total, how many entries were scanned, and whether
/// either cap was hit.</summary>
public sealed record PreviewResult(
    IReadOnlyList<PreviewMatch> Matches,
    int TotalMatched,
    int EntriesScanned,
    bool Truncated);

/// <summary>
/// Finds every file or folder a rule would currently act on, without acting
/// on any of them - pure read-only enumeration and evaluation via
/// <see cref="RuleEngine.ShouldProcess"/>, the same check the real sweep uses.
/// Safe to run against a rule that isn't saved or enabled yet.
/// </summary>
public static class DryRunPreview
{
    // Safety caps so pointing a recursive rule at something huge (e.g. a whole
    // drive) can't hang the UI or return an unbounded list.
    private const int MaxEntriesToScan = 50_000;
    private const int MaxMatchesToShow = 500;

    public static PreviewResult Run(Rule rule)
    {
        var shown = new List<PreviewMatch>();
        int scanned = 0, totalMatched = 0;
        bool truncated = false;

        if (!string.IsNullOrWhiteSpace(rule.SourceFolder) && Directory.Exists(rule.SourceFolder))
        {
            var option = rule.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            bool wantFolders = rule.Target == MatchTarget.Folders;

            IEnumerable<string> paths;
            try
            {
                paths = wantFolders
                    ? Directory.EnumerateDirectories(rule.SourceFolder, "*", option)
                    : Directory.EnumerateFiles(rule.SourceFolder, "*", option);
            }
            catch { paths = Enumerable.Empty<string>(); }

            foreach (var path in paths)
            {
                if (++scanned > MaxEntriesToScan) { truncated = true; break; }

                FileSystemInfo entry;
                try
                {
                    entry = wantFolders ? new DirectoryInfo(path) : new FileInfo(path);
                    if (!entry.Exists) continue;
                }
                catch { continue; }

                if (!RuleEngine.ShouldProcess(rule, entry)) continue;

                totalMatched++;
                if (shown.Count < MaxMatchesToShow)
                    shown.Add(new PreviewMatch(entry, ActionRunner.DescribeAction(rule, entry)));
                else
                    truncated = true;
            }
        }

        return new PreviewResult(shown, totalMatched, scanned, truncated);
    }
}

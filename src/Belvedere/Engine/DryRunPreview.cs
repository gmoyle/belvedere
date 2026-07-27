using Belvedere.Models;

namespace Belvedere.Engine;

/// <summary>A single file a rule would currently act on, and a description of
/// what that action would be. Preview-only - no filesystem changes.</summary>
public sealed record PreviewMatch(FileInfo File, string ActionDescription);

/// <summary>The outcome of a dry run: the matches found (capped for display),
/// how many matched in total, how many files were scanned, and whether either
/// cap was hit.</summary>
public sealed record PreviewResult(
    IReadOnlyList<PreviewMatch> Matches,
    int TotalMatched,
    int FilesScanned,
    bool Truncated);

/// <summary>
/// Finds every file a rule would currently act on, without acting on any of
/// them - pure read-only enumeration and evaluation via
/// <see cref="RuleEngine.ShouldProcess"/>, the same check the real sweep uses.
/// Safe to run against a rule that isn't saved or enabled yet.
/// </summary>
public static class DryRunPreview
{
    // Safety caps so pointing a recursive rule at something huge (e.g. a whole
    // drive) can't hang the UI or return an unbounded list.
    private const int MaxFilesToScan = 50_000;
    private const int MaxMatchesToShow = 500;

    public static PreviewResult Run(Rule rule)
    {
        var shown = new List<PreviewMatch>();
        int scanned = 0, totalMatched = 0;
        bool truncated = false;

        if (!string.IsNullOrWhiteSpace(rule.SourceFolder) && Directory.Exists(rule.SourceFolder))
        {
            var option = rule.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            IEnumerable<string> paths;
            try { paths = Directory.EnumerateFiles(rule.SourceFolder, "*", option); }
            catch { paths = Enumerable.Empty<string>(); }

            foreach (var path in paths)
            {
                if (++scanned > MaxFilesToScan) { truncated = true; break; }

                FileInfo file;
                try { file = new FileInfo(path); if (!file.Exists) continue; }
                catch { continue; }

                if (!RuleEngine.ShouldProcess(rule, file)) continue;

                totalMatched++;
                if (shown.Count < MaxMatchesToShow)
                    shown.Add(new PreviewMatch(file, ActionRunner.DescribeAction(rule, file)));
                else
                    truncated = true;
            }
        }

        return new PreviewResult(shown, totalMatched, scanned, truncated);
    }
}

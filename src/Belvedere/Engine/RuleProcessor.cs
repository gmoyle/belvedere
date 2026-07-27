using Belvedere.Models;
using Belvedere.Services;

namespace Belvedere.Engine;

/// <summary>
/// Runs rules against their source folders: enumerate files, evaluate, act,
/// log, and notify. This is the modern equivalent of the AHK main loop body.
/// </summary>
public sealed class RuleProcessor
{
    private readonly Logger _log;

    /// <summary>Called when a matched action succeeds (for toast notifications).</summary>
    public Action<string>? OnActionNotify { get; set; }

    /// <summary>Called on error (for toast notifications).</summary>
    public Action<string>? OnErrorNotify { get; set; }

    /// <summary>Confirmation gate for rules with ConfirmAction=true. Returns true
    /// to proceed. If null, confirmation-required actions are skipped for safety.</summary>
    public Func<Rule, FileSystemInfo, bool>? ConfirmPrompt { get; set; }

    public RuleProcessor(Logger log) => _log = log;

    public void ProcessAll(IEnumerable<Rule> rules)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;
            try { ProcessRule(rule); }
            catch (Exception ex) { _log.Error($"Rule '{rule.Name}' failed: {ex.Message}"); }
        }
    }

    public void ProcessRule(Rule rule)
    {
        if (!rule.Enabled) return;
        if (string.IsNullOrWhiteSpace(rule.SourceFolder) || !Directory.Exists(rule.SourceFolder))
            return;

        var option = rule.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        // Snapshot first: acting on entries mutates the folder while enumerating.
        // (A matched parent folder can also carry away subfolders that were
        // independently snapshotted too; the FileSystemInfo.Exists check below
        // quietly skips those rather than erroring on a path that just moved.)
        List<string> paths;
        try
        {
            paths = rule.Target == MatchTarget.Folders
                ? Directory.EnumerateDirectories(rule.SourceFolder, "*", option).ToList()
                : Directory.EnumerateFiles(rule.SourceFolder, "*", option).ToList();
        }
        catch (Exception ex)
        {
            _log.Error($"Cannot read '{rule.SourceFolder}': {ex.Message}");
            return;
        }

        foreach (var path in paths)
        {
            FileSystemInfo entry;
            try
            {
                entry = rule.Target == MatchTarget.Folders ? new DirectoryInfo(path) : new FileInfo(path);
                if (!entry.Exists) continue;
            }
            catch { continue; }

            if (!RuleEngine.ShouldProcess(rule, entry)) continue;

            if (rule.ConfirmAction)
            {
                bool proceed = ConfirmPrompt?.Invoke(rule, entry) ?? false;
                if (!proceed) continue;
            }

            var result = ActionRunner.Run(rule, entry);
            string header = $"{rule.Action.Label()}: {entry.Name}";

            switch (result.Outcome)
            {
                case ActionOutcome.Success:
                    _log.Action($"{header} — {result.Message}");
                    OnActionNotify?.Invoke($"{header}\n{result.Message}");
                    break;
                case ActionOutcome.Skipped:
                    _log.Info($"{header} — skipped: {result.Message}");
                    break;
                case ActionOutcome.Failed:
                    _log.Error($"{header} — {result.Message}");
                    OnErrorNotify?.Invoke($"{header}\n{result.Message}");
                    break;
            }
        }
    }
}

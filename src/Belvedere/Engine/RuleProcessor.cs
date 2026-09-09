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

    /// <summary>Total time, per sweep, that all rules combined may spend waiting
    /// on locked files before deferring the rest to a later sweep. Bounds how
    /// long a single sweep can stall when many files are locked at once.</summary>
    private const int RetryBudgetPerSweepMs = 5000;

    public RuleProcessor(Logger log) => _log = log;

    /// <summary>Runs every enabled rule and returns how many entries were
    /// deferred because they were locked by another process. A non-zero count
    /// tells the caller to schedule a prompt retry rather than waiting for the
    /// next periodic sweep.</summary>
    public int ProcessAll(IEnumerable<Rule> rules)
    {
        var ruleList = rules as IReadOnlyList<Rule> ?? rules.ToList();

        // Read the Windows "opened by me" history once per sweep, and only when a
        // rule actually needs it - scanning it isn't free, and most rules don't.
        var opens = NeedsUsageHistory(ruleList) ? UsageHistory.Snapshot(_log) : null;

        // One shared wait budget for the whole sweep, so many locked files can't
        // each add their own multi-second retry wait.
        var budget = new RetryBudget(RetryBudgetPerSweepMs);

        int deferred = 0;
        foreach (var rule in ruleList)
        {
            if (!rule.Enabled) continue;
            try { deferred += ProcessRule(rule, opens, budget); }
            catch (Exception ex) { _log.Error($"Rule '{rule.Name}' failed: {ex.Message}"); }
        }
        return deferred;
    }

    private static bool NeedsUsageHistory(IEnumerable<Rule> rules) =>
        rules.Any(r => r.Enabled && r.Conditions.Any(c => c.Subject == Subject.DateUsed));

    /// <summary>Processes a single rule and returns the number of entries that
    /// were deferred (locked by another process). An optional shared
    /// <paramref name="budget"/> caps total retry-waiting across a sweep.</summary>
    public int ProcessRule(Rule rule, IReadOnlyDictionary<string, DateTime>? opens = null, RetryBudget? budget = null)
    {
        if (!rule.Enabled) return 0;
        if (string.IsNullOrWhiteSpace(rule.SourceFolder) || !Directory.Exists(rule.SourceFolder))
            return 0;

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
            return 0;
        }

        int deferred = 0;
        foreach (var path in paths)
        {
            FileSystemInfo entry;
            try
            {
                entry = rule.Target == MatchTarget.Folders ? new DirectoryInfo(path) : new FileInfo(path);
                if (!entry.Exists) continue;
            }
            catch { continue; }

            if (!RuleEngine.ShouldProcess(rule, entry, opens)) continue;

            if (rule.ConfirmAction)
            {
                bool proceed = ConfirmPrompt?.Invoke(rule, entry) ?? false;
                if (!proceed) continue;
            }

            var result = ActionRunner.Run(rule, entry, budget);
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
                case ActionOutcome.Deferred:
                    // Transient lock, not a failure: log quietly, no error toast,
                    // and count it so the sweep can be retried shortly.
                    deferred++;
                    _log.Info($"{header} — deferred: {result.Message}");
                    break;
                case ActionOutcome.Failed:
                    _log.Error($"{header} — {result.Message}");
                    OnErrorNotify?.Invoke($"{header}\n{result.Message}");
                    break;
            }
        }

        return deferred;
    }
}

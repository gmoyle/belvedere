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
    public Func<Rule, FileInfo, bool>? ConfirmPrompt { get; set; }

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

        // Snapshot first: acting on files mutates the folder while enumerating.
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(rule.SourceFolder, "*", option).ToList();
        }
        catch (Exception ex)
        {
            _log.Error($"Cannot read '{rule.SourceFolder}': {ex.Message}");
            return;
        }

        foreach (var path in files)
        {
            FileInfo file;
            try { file = new FileInfo(path); if (!file.Exists) continue; }
            catch { continue; }

            if (!RuleEngine.ShouldProcess(rule, file)) continue;

            if (rule.ConfirmAction)
            {
                bool proceed = ConfirmPrompt?.Invoke(rule, file) ?? false;
                if (!proceed) continue;
            }

            var result = ActionRunner.Run(rule, file);
            string header = $"{rule.Action.Label()}: {file.Name}";

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

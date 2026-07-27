using Belvedere.Engine;
using Belvedere.Models;

namespace Belvedere.Services;

/// <summary>A rule imported from rules.ini whose destination folder could not
/// be created, and was therefore disabled rather than left silently broken.</summary>
public sealed record ImportWarning(string RuleName, string Destination, string Reason);

public sealed record ImportResult(AppConfig Config, IReadOnlyList<ImportWarning> Warnings);

/// <summary>
/// Migrates a legacy Belvedere <c>rules.ini</c> into the modern
/// <see cref="AppConfig"/> model, so long-time users keep their rules.
/// </summary>
public static class IniImporter
{
    public static ImportResult Import(string iniPath)
    {
        var ini = Ini.Parse(File.ReadAllLines(iniPath));
        var config = new AppConfig();
        var warnings = new List<ImportWarning>();

        // Preferences ---------------------------------------------------------
        if (ini.TryGetValue("Preferences", out var prefs))
        {
            if (int.TryParse(prefs.GetValueOrDefault("Sleeptime"), out int sleep) && sleep > 0)
                config.SweepInterval = sleep;
            config.SweepUnit = Display.ParseTimeUnit(prefs.GetValueOrDefault("SleeptimeLength", "minutes"));
            config.EnableLogging = prefs.GetValueOrDefault("EnableLogging") == "1";
            config.ConfirmExit = prefs.GetValueOrDefault("ConfirmExit", "1") == "1";
            config.ShowNotifications = prefs.GetValueOrDefault("TrayTipEnabled") == "1";
        }

        // Rules ---------------------------------------------------------------
        string allRuleNames = ini.GetValueOrDefault("Rules")?.GetValueOrDefault("AllRuleNames") ?? string.Empty;
        foreach (var name in SplitPipe(allRuleNames))
        {
            if (!ini.TryGetValue(name, out var sec)) continue;
            var rule = ParseRule(name, sec);
            if (rule is null) continue;

            config.Rules.Add(rule);

            var warning = EnsureDestinationExists(rule);
            if (warning is not null) warnings.Add(warning);
        }

        return new ImportResult(config, warnings);
    }

    /// <summary>Best-effort: create a rule's destination folder if it's missing,
    /// so an imported rule isn't silently broken just because the old machine's
    /// folder layout doesn't exist yet on this one. If it can't be created, the
    /// rule is disabled (rather than left enabled and silently failing on every
    /// sweep) and a warning is returned so the caller can tell the user.</summary>
    private static ImportWarning? EnsureDestinationExists(Rule rule)
    {
        bool usesFolder = rule.Action is ActionType.Move or ActionType.MoveLeaveShortcut or ActionType.Copy;
        if (!usesFolder || string.IsNullOrWhiteSpace(rule.Destination)) return null;

        if (DestinationFolder.TryEnsure(rule.Destination, out string error)) return null;

        rule.Enabled = false;
        return new ImportWarning(rule.Name, rule.Destination, error);
    }

    /// <summary>Builds a user-facing summary of an import, including any rules
    /// that had to be disabled because their destination folder could not be
    /// created. Shared by every UI entry point that imports a rules.ini.</summary>
    public static string FormatSummary(ImportResult result)
    {
        string summary = $"Imported {result.Config.Rules.Count} rule(s).";
        if (result.Warnings.Count == 0) return summary;

        var lines = result.Warnings.Select(w => $"  • \"{w.RuleName}\" → {w.Destination}");
        return summary +
            "\n\nThe following rule(s) could not create their destination folder, " +
            "so they were imported disabled. Fix the destination and re-enable them under Manage:\n" +
            string.Join("\n", lines);
    }

    private static Rule? ParseRule(string name, Dictionary<string, string> sec)
    {
        string folder = sec.GetValueOrDefault("Folder", string.Empty).Trim();
        if (folder.Length == 0) return null;

        // Legacy stored the folder as a glob, e.g. "C:\Desktop\*".
        string source = folder;
        if (source.EndsWith("\\*")) source = source[..^2];
        else if (source.EndsWith("*")) source = source[..^1];
        source = source.TrimEnd('\\');

        var rule = new Rule
        {
            Name = name,
            SourceFolder = source,
            Enabled = sec.GetValueOrDefault("Enabled") == "1",
            ConfirmAction = sec.GetValueOrDefault("ConfirmAction") == "1",
            Recursive = sec.GetValueOrDefault("Recursive") == "1",
            Match = string.Equals(sec.GetValueOrDefault("Matches", "ALL"), "ANY", StringComparison.OrdinalIgnoreCase)
                ? MatchMode.Any : MatchMode.All,
            Action = Display.ParseAction(sec.GetValueOrDefault("Action", "Move file")) ?? ActionType.Move,
            Destination = sec.GetValueOrDefault("Destination", string.Empty).Trim(),
            Overwrite = sec.GetValueOrDefault("Overwrite") == "1",
            SkipReadOnly = sec.GetValueOrDefault("AttribReadOnly") == "1",
            SkipHidden = sec.GetValueOrDefault("AttribHidden") == "1",
            SkipSystem = sec.GetValueOrDefault("AttribSystem") == "1",
        };

        // Conditions: first uses unsuffixed keys, then Subject1/Verb1/... etc.
        for (int i = 0; ; i++)
        {
            string suffix = i == 0 ? string.Empty : i.ToString();
            if (!sec.TryGetValue("Subject" + suffix, out string? subjRaw)) break;

            var subject = Display.ParseSubject(subjRaw);
            var verb = Display.ParseVerb(sec.GetValueOrDefault("Verb" + suffix, string.Empty));
            if (subject is null || verb is null) continue;

            var cond = new Condition
            {
                Subject = subject.Value,
                Verb = verb.Value,
                Value = sec.GetValueOrDefault("Object" + suffix, string.Empty).Trim(),
            };

            string units = sec.GetValueOrDefault("Units" + suffix, string.Empty);
            if (subject.Value == Subject.Size)
                cond.SizeUnit = Display.ParseSizeUnit(units);
            else if (subject.Value.IsDateSubject())
                cond.TimeUnit = Display.ParseTimeUnit(units);

            rule.Conditions.Add(cond);
        }

        return rule;
    }

    private static IEnumerable<string> SplitPipe(string value) =>
        value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Minimal case-insensitive INI parser (sections + key=value).</summary>
internal static class Ini
{
    public static Dictionary<string, Dictionary<string, string>> Parse(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;

        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string section = line[1..^1].Trim();
                if (!result.TryGetValue(section, out current))
                    result[section] = current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0 || current is null) continue;

            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim();
            current[key] = val;
        }

        return result;
    }
}

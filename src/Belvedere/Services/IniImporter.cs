using Belvedere.Models;

namespace Belvedere.Services;

/// <summary>
/// Migrates a legacy Belvedere <c>rules.ini</c> into the modern
/// <see cref="AppConfig"/> model, so long-time users keep their rules.
/// </summary>
public static class IniImporter
{
    public static AppConfig Import(string iniPath)
    {
        var ini = Ini.Parse(File.ReadAllLines(iniPath));
        var config = new AppConfig();

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
            if (rule is not null) config.Rules.Add(rule);
        }

        return config;
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

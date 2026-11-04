using Belvedere.Models;

namespace Belvedere.Services;

/// <summary>
/// Converts a simple "archive stale files, but keep these" <see cref="Rule"/> into
/// the review-first <see cref="ManagedFolder"/> model. The whole point of the new
/// model is that you no longer hand-author a pile of exclusion conditions - so on
/// upgrade we lift an existing archive rule into it automatically: its age
/// condition becomes the folder's idle threshold, and each "Extension is not x" /
/// "Name does not contain y" exclusion becomes a standing "Keep" decision.
///
/// Only rules that match this exact shape convert; anything more exotic is left as
/// a classic rule, untouched.
/// </summary>
public static class ManagedFolderConverter
{
    public static bool IsConvertible(Rule rule) => TryConvert(rule, out _);

    public static bool TryConvert(Rule rule, out ManagedFolder folder)
    {
        folder = new ManagedFolder();

        // Must be a move-to-folder archive over files, with its exclusions ANDed.
        if (rule.Action != ActionType.Move) return false;
        if (rule.Target != MatchTarget.Files) return false;
        if (rule.Match != MatchMode.All) return false;
        if (string.IsNullOrWhiteSpace(rule.SourceFolder) || string.IsNullOrWhiteSpace(rule.Destination))
            return false;

        // Exactly one "age" condition defines the idle threshold.
        var ageConds = rule.Conditions
            .Where(c => (c.Subject is Subject.DateUsed or Subject.DateModified) && c.Verb == Verb.IsNotInTheLast)
            .ToList();
        if (ageConds.Count != 1) return false;

        var age = ageConds[0];
        if (!int.TryParse(age.Value, out int ageValue) || ageValue < 0) return false;

        // Every other condition must be an exclusion we can express as a "Keep".
        var decisions = new List<ArchiveDecision>();
        foreach (var c in rule.Conditions)
        {
            if (ReferenceEquals(c, age)) continue;
            if (!TryMapExclusion(c, decisions)) return false;
        }

        folder = new ManagedFolder
        {
            Enabled = rule.Enabled,
            Path = rule.SourceFolder,
            Destination = rule.Destination,
            MaxIdleAge = ageValue,
            MaxIdleUnit = age.TimeUnit,
            Basis = age.Subject == Subject.DateUsed ? IdleBasis.Used : IdleBasis.Modified,
            Decisions = decisions,
        };
        return true;
    }

    private static bool TryMapExclusion(Condition c, List<ArchiveDecision> into)
    {
        switch (c.Subject, c.Verb)
        {
            case (Subject.Extension, Verb.IsNot):
                into.Add(Keep(DecisionScope.Extension, c.Value.TrimStart('.')));
                return true;

            case (Subject.Extension, Verb.DoesNotMatchOneOf):
                foreach (var ext in Split(c.Value))
                    into.Add(Keep(DecisionScope.Extension, ext.TrimStart('.')));
                return true;

            case (Subject.Name, Verb.DoesNotContain):
                into.Add(Keep(DecisionScope.Glob, $"*{c.Value}*"));
                return true;

            default:
                return false; // anything else: not safely convertible
        }
    }

    private static ArchiveDecision Keep(DecisionScope scope, string target) =>
        new() { Kind = DecisionKind.KeepForever, Scope = scope, Target = target };

    private static IEnumerable<string> Split(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A short human summary of the conversion, for the upgrade prompt.</summary>
    public static string Describe(ManagedFolder folder)
    {
        string keeps = folder.Decisions.Count == 0
            ? "nothing kept yet"
            : "keeping " + string.Join(", ", folder.Decisions.Select(d =>
                d.Scope == DecisionScope.Extension ? "." + d.Target : d.Target));

        return $"Watch: {folder.Path}\n" +
               $"Archive to: {folder.Destination}\n" +
               $"When unused for: {folder.MaxIdleAge} {folder.MaxIdleUnit.ToString().ToLowerInvariant()} " +
               $"({(folder.Basis == IdleBasis.Used ? "last used" : "last modified")})\n" +
               $"Already {keeps}";
    }
}

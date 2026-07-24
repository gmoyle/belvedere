namespace Belvedere.Models;

/// <summary>
/// One "subject / verb / object" test, e.g. "Extension is jpg" or
/// "Date modified is not in the last 8 weeks". Mirrors the original
/// Belvedere rule vocabulary.
/// </summary>
public sealed class Condition
{
    public Subject Subject { get; set; } = Subject.Name;
    public Verb Verb { get; set; } = Verb.Is;

    /// <summary>The comparison value (the "object"). For list verbs this is a
    /// comma-separated set; for Size/date verbs it is a number.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Unit for Size comparisons (ignored otherwise).</summary>
    public SizeUnit SizeUnit { get; set; } = SizeUnit.MB;

    /// <summary>Unit for "in the last" date comparisons (ignored otherwise).</summary>
    public TimeUnit TimeUnit { get; set; } = TimeUnit.Days;
}

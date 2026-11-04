using System.Text.Json;
using System.Text.Json.Serialization;

namespace Belvedere.Engine;

/// <summary>One moved item: where it came from, where it went, and whether it was
/// a whole folder. Enough to put it back.</summary>
public sealed class JournalEntry
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public bool WasFolder { get; set; }
}

/// <summary>One archive action the user approved - all the items moved together,
/// so "Undo last archive" reverses the whole batch.</summary>
public sealed class JournalBatch
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string ManagedRoot { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public List<JournalEntry> Entries { get; set; } = new();
}

/// <summary>
/// A small, persisted history of archive moves, so any archive can be undone. Kept
/// as JSON next to the config (<c>%AppData%\Belvedere\archive-journal.json</c>),
/// capped to the most recent batches. Persistence only - the actual move-back is
/// done by <see cref="ArchiveApplier.UndoLast"/>.
/// </summary>
public sealed class ArchiveJournal
{
    private const int MaxBatches = 20;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private List<JournalBatch> _batches = new();

    public ArchiveJournal(string dataFolder)
    {
        _path = Path.Combine(dataFolder, "archive-journal.json");
        Load();
    }

    public bool HasEntries => _batches.Count > 0;
    public JournalBatch? Last => _batches.Count > 0 ? _batches[^1] : null;
    public IReadOnlyList<JournalBatch> Batches => _batches;

    public void Append(JournalBatch batch)
    {
        if (batch.Entries.Count == 0) return;
        _batches.Add(batch);
        if (_batches.Count > MaxBatches)
            _batches.RemoveRange(0, _batches.Count - MaxBatches);
        Save();
    }

    public void RemoveLast()
    {
        if (_batches.Count == 0) return;
        _batches.RemoveAt(_batches.Count - 1);
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            _batches = JsonSerializer.Deserialize<List<JournalBatch>>(File.ReadAllText(_path), Options) ?? new();
        }
        catch { _batches = new(); } // a broken journal must never block archiving
    }

    private void Save()
    {
        try
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_batches, Options));
            File.Move(tmp, _path, overwrite: true);
        }
        catch { /* best effort; undo is a convenience, not critical to archiving */ }
    }
}

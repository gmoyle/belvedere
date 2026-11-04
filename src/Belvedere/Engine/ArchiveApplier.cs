using Belvedere.Models;
using Microsoft.VisualBasic.FileIO;

namespace Belvedere.Engine;

/// <summary>The outcome of applying approved proposals.</summary>
public sealed record ApplyResult(int FilesMoved, int FoldersMoved, int Failed, IReadOnlyList<string> Errors)
{
    public int Total => FilesMoved + FoldersMoved;
}

/// <summary>The outcome of undoing the last archive batch.</summary>
public sealed record UndoResult(int Restored, int Skipped, IReadOnlyList<string> Errors);

/// <summary>
/// Performs the archive moves the user approved in review - and reverses them.
/// Unlike the classic <see cref="ActionRunner"/>, which flattens everything into
/// one destination folder, this preserves each item's position relative to the
/// managed root (so <c>Desktop\ProjectX\notes.txt</c> lands at
/// <c>Archive\ProjectX\notes.txt</c>, duplicate names never collide, and emptied
/// source folders are cleaned up). Every move is recorded in the journal so it can
/// be undone.
/// </summary>
public static class ArchiveApplier
{
    // Same "file is temporarily locked (OneDrive/an app saving)" handling as
    // ActionRunner: a few short retries before giving up on that one item.
    private const int MaxAttempts = 4;
    private const int RetryDelayMs = 300;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    public static ApplyResult Apply(ManagedFolder folder, IReadOnlyList<ArchiveProposal> approved, ArchiveJournal journal)
    {
        var errors = new List<string>();
        if (approved.Count == 0)
            return new ApplyResult(0, 0, 0, errors);

        if (!DestinationFolder.TryEnsure(folder.Destination, out var destError))
            return new ApplyResult(0, 0, approved.Count, new[] { destError });

        string root = folder.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int files = 0, folders = 0, failed = 0;
        var batch = new JournalBatch { ManagedRoot = root, Destination = folder.Destination };

        foreach (var p in approved)
        {
            try
            {
                string target = UniqueTarget(Path.Combine(folder.Destination, p.RelativePath), p.Kind == ProposalKind.Folder);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (p.Kind == ProposalKind.Folder)
                {
                    FileSystem.MoveDirectory(p.Entry.FullName, target);
                    batch.Entries.Add(new JournalEntry { From = p.Entry.FullName, To = target, WasFolder = true });
                    folders++;
                }
                else
                {
                    string sourceDir = Path.GetDirectoryName(p.Entry.FullName)!;
                    MoveFileWithRetry(p.Entry.FullName, target);
                    batch.Entries.Add(new JournalEntry { From = p.Entry.FullName, To = target, WasFolder = false });
                    files++;
                    RemoveEmptyParents(sourceDir, root);
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{p.Entry.Name}: {ex.Message}");
            }
        }

        journal.Append(batch);
        return new ApplyResult(files, folders, failed, errors);
    }

    /// <summary>Reverses the most recent archive batch, putting each item back where
    /// it came from. Items whose origin has since been reoccupied, or whose archived
    /// copy is gone, are skipped rather than overwriting anything.</summary>
    public static UndoResult UndoLast(ArchiveJournal journal)
    {
        var batch = journal.Last;
        if (batch is null) return new UndoResult(0, 0, Array.Empty<string>());

        int restored = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var e in batch.Entries)
        {
            bool existsAtDest = e.WasFolder ? Directory.Exists(e.To) : File.Exists(e.To);
            bool originFree = !(e.WasFolder ? Directory.Exists(e.From) : File.Exists(e.From));

            if (!existsAtDest) { skipped++; continue; }           // archived copy already gone
            if (!originFree) { skipped++; continue; }             // original spot taken again

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(e.From)!);
                if (e.WasFolder) FileSystem.MoveDirectory(e.To, e.From);
                else MoveFileWithRetry(e.To, e.From);
                restored++;
            }
            catch (Exception ex)
            {
                skipped++;
                errors.Add($"{Path.GetFileName(e.From)}: {ex.Message}");
            }
        }

        journal.RemoveLast();
        return new UndoResult(restored, skipped, errors);
    }

    // -- Helpers --------------------------------------------------------------

    /// <summary>Returns the target path if free, otherwise the same name with a
    /// " (2)", " (3)"… suffix - so archiving never silently overwrites or drops a
    /// file whose name already exists at the destination.</summary>
    private static string UniqueTarget(string path, bool isDir)
    {
        bool Taken(string p) => isDir ? Directory.Exists(p) : File.Exists(p);
        if (!Taken(path)) return path;

        string dir = Path.GetDirectoryName(path)!;
        string stem = isDir ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        string ext = isDir ? string.Empty : Path.GetExtension(path);

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!Taken(candidate)) return candidate;
        }
        throw new IOException($"Could not find a free name for {path}");
    }

    private static void MoveFileWithRetry(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { File.Move(from, to); return; }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < MaxAttempts)
            {
                Thread.Sleep(RetryDelayMs * attempt);
            }
        }
    }

    private static bool IsSharingViolation(IOException ex)
    {
        int code = ex.HResult & 0xFFFF;
        return code is ErrorSharingViolation or ErrorLockViolation;
    }

    /// <summary>Deletes <paramref name="startDir"/> and its ancestors while they're
    /// empty, stopping before the managed root - so archiving loose files out of a
    /// subfolder doesn't leave an empty husk behind.</summary>
    private static void RemoveEmptyParents(string startDir, string root)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null
            && dir.Exists
            && !PathEquals(dir.FullName, root)
            && IsStrictlyWithin(dir.FullName, root))
        {
            try
            {
                if (dir.EnumerateFileSystemInfos().Any()) break; // not empty
                var parent = dir.Parent;
                dir.Delete(recursive: false);
                dir = parent;
            }
            catch { break; } // leave it rather than fight a lock
        }
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static bool IsStrictlyWithin(string path, string root)
    {
        string r = root.TrimEnd('\\', '/');
        return path.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

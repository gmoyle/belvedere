using System.Runtime.InteropServices;

namespace Belvedere.Services;

/// <summary>
/// Answers "when did the user last actually <em>open</em> this file?" by reading
/// the Windows "Recent Items" history (the <c>.lnk</c> shortcuts the shell drops
/// in <c>%AppData%\Microsoft\Windows\Recent</c> whenever you open a file through
/// Explorer or most apps).
///
/// This exists because NTFS last-access time (<see cref="FileSystemInfo.LastAccessTime"/>)
/// is useless as an "opened by me" signal on a modern machine: OneDrive sync,
/// Windows Search indexing, antivirus, and even a folder sweep all bump it, so
/// everything looks freshly accessed. The Recent history, by contrast, is written
/// only by genuine user opens - which is exactly what lets a "not opened in the
/// last N weeks" rule work on a cloud-synced folder.
///
/// Best-effort throughout: if the history can't be read (feature disabled by
/// policy, COM unavailable, etc.) the snapshot is simply empty, and callers fall
/// back to the file's own timestamps.
/// </summary>
public static class UsageHistory
{
    private static readonly object Gate = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static IReadOnlyDictionary<string, DateTime>? _cache;
    private static DateTime _builtAt;

    /// <summary>
    /// A map of full path (case-insensitive) → the most recent time the user
    /// opened it, per the Windows Recent Items shortcuts. Cached briefly so
    /// back-to-back sweeps don't re-scan. A path that isn't present simply has no
    /// recorded open.
    /// </summary>
    public static IReadOnlyDictionary<string, DateTime> Snapshot(Logger? log = null)
    {
        lock (Gate)
        {
            if (_cache is not null && DateTime.Now - _builtAt < CacheTtl)
                return _cache;

            _cache = Build(log);
            _builtAt = DateTime.Now;
            return _cache;
        }
    }

    private static Dictionary<string, DateTime> Build(Logger? log)
    {
        var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        string recent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Recent");
        if (!Directory.Exists(recent)) return map;

        string[] links;
        try { links = Directory.GetFiles(recent, "*.lnk"); }
        catch { return map; }

        object? shell = null;
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return map;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return map;
            dynamic sh = shell;

            foreach (var lnk in links)
            {
                try
                {
                    string target = sh.CreateShortcut(lnk).TargetPath as string ?? string.Empty;
                    if (target.Length == 0) continue;

                    // The shortcut's own last-write time is when the shell recorded
                    // this open - a truer "opened at" than anything on the file.
                    DateTime opened = File.GetLastWriteTime(lnk);

                    if (!map.TryGetValue(target, out var existing) || opened > existing)
                        map[target] = opened;
                }
                catch { /* skip an unreadable or odd shortcut; keep going */ }
            }
        }
        catch (Exception ex)
        {
            log?.Error("Could not read Windows Recent Items history: " + ex.Message);
        }
        finally
        {
            if (shell is not null)
            {
                try { Marshal.FinalReleaseComObject(shell); } catch { }
            }
        }

        return map;
    }
}

using Belvedere.Engine;
using Belvedere.Models;

namespace Belvedere.Services;

/// <summary>
/// Drives rule processing. Two triggers:
///   1. Real-time: a FileSystemWatcher per watched folder, debounced so a burst
///      of changes coalesces into one sweep.
///   2. Periodic: a timer that sweeps everything on the configured interval — a
///      safety net (covers date-based rules and anything missed while paused).
/// Sweeps are serialized so they never overlap.
/// </summary>
public sealed class WatchService : IDisposable
{
    private readonly Logger _log;
    private readonly RuleProcessor _processor;
    private readonly object _gate = new();

    private AppConfig _config = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private System.Threading.Timer? _sweepTimer;
    private System.Threading.Timer? _debounceTimer;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private volatile bool _paused;
    private volatile bool _started;

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);

    public WatchService(Logger log, RuleProcessor processor)
    {
        _log = log;
        _processor = processor;
    }

    public bool IsPaused => _paused;

    public void Start(AppConfig config)
    {
        _config = config;
        _started = true;
        Rebuild();
        _log.Info("Watching started.");
        RequestSweep(); // initial pass on launch
    }

    /// <summary>Re-read configuration (call after the user edits rules/prefs).</summary>
    public void Reload(AppConfig config)
    {
        _config = config;
        if (_started) Rebuild();
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        _log.Info(paused ? "Paused." : "Resumed.");
        if (!paused) RequestSweep();
    }

    /// <summary>Run all rules right now (used by "Run now" and on launch).</summary>
    public void RunNow() => RunSweep();

    private void Rebuild()
    {
        lock (_gate)
        {
            DisposeWatchers();
            StopTimers();

            // Periodic sweep timer.
            long ms = (long)(_config.SweepInterval * RuleEngineSeconds(_config.SweepUnit) * 1000);
            if (ms < 1000) ms = 1000;
            _sweepTimer = new System.Threading.Timer(_ => RequestSweep(), null, ms, ms);

            if (!_config.RealTimeWatch) return;

            // One watcher per distinct, existing source folder.
            var folders = _config.Rules
                .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.SourceFolder))
                .GroupBy(r => r.SourceFolder, StringComparer.OrdinalIgnoreCase);

            foreach (var group in folders)
            {
                string folder = group.Key;
                if (!Directory.Exists(folder)) continue;

                try
                {
                    var w = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = group.Any(r => r.Recursive),
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                                     | NotifyFilters.CreationTime | NotifyFilters.Size,
                        EnableRaisingEvents = true,
                    };
                    w.Created += OnFsEvent;
                    w.Changed += OnFsEvent;
                    w.Renamed += OnFsEvent;
                    w.Error += OnFsError;
                    _watchers.Add(w);
                }
                catch (Exception ex)
                {
                    _log.Error($"Could not watch '{folder}': {ex.Message}");
                }
            }
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => RequestSweep();

    private void OnFsError(object sender, ErrorEventArgs e) =>
        _log.Error($"Folder watcher error: {e.GetException().Message}");

    /// <summary>Coalesce triggers: (re)start the debounce timer; the sweep runs
    /// once things go quiet.</summary>
    private void RequestSweep()
    {
        lock (_gate)
        {
            _debounceTimer ??= new System.Threading.Timer(_ => RunSweep(), null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void RunSweep()
    {
        if (_paused || !_started) return;

        // Skip if a sweep is already running; the periodic timer will catch up.
        if (!_runLock.Wait(0)) return;
        try
        {
            _processor.ProcessAll(_config.Rules);
        }
        catch (Exception ex)
        {
            _log.Error($"Sweep failed: {ex.Message}");
        }
        finally
        {
            _runLock.Release();
        }
    }

    private static double RuleEngineSeconds(TimeUnit unit) => RuleEngine.UnitToSeconds(unit);

    private void DisposeWatchers()
    {
        foreach (var w in _watchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
        }
        _watchers.Clear();
    }

    private void StopTimers()
    {
        _sweepTimer?.Dispose(); _sweepTimer = null;
        _debounceTimer?.Dispose(); _debounceTimer = null;
    }

    public void Dispose()
    {
        _started = false;
        lock (_gate)
        {
            DisposeWatchers();
            StopTimers();
        }
        _runLock.Dispose();
    }
}

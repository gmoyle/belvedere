using System.Text;

namespace Belvedere.Services;

public enum LogCategory { System, Action, Error }

public sealed record LogEntry(DateTime Timestamp, LogCategory Category, string Message)
{
    public override string ToString() =>
        $"{Timestamp:yyyy-MM-dd HH:mm:ss}  [{Category}]  {Message}";
}

/// <summary>
/// Thread-safe file + in-memory logger. Raises <see cref="Logged"/> so the UI
/// (log window, toasts) can react without polling.
/// </summary>
public sealed class Logger
{
    private readonly object _gate = new();
    private readonly string _logPath;
    private bool _enabled = true;

    public event EventHandler<LogEntry>? Logged;

    public Logger(string logPath) => _logPath = logPath;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public string LogPath => _logPath;

    public void Log(string message, LogCategory category = LogCategory.System)
    {
        var entry = new LogEntry(DateTime.Now, category, message);

        if (_enabled)
        {
            try
            {
                lock (_gate)
                {
                    File.AppendAllText(_logPath, entry + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never take the app down.
            }
        }

        Logged?.Invoke(this, entry);
    }

    public void Info(string message) => Log(message, LogCategory.System);
    public void Action(string message) => Log(message, LogCategory.Action);
    public void Error(string message) => Log(message, LogCategory.Error);

    public string ReadAll()
    {
        lock (_gate)
        {
            return File.Exists(_logPath) ? File.ReadAllText(_logPath, Encoding.UTF8) : string.Empty;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_logPath)) File.Delete(_logPath);
        }
    }
}

namespace Belvedere.Models;

/// <summary>
/// The full persisted configuration: preferences plus all rules. Serialized to
/// JSON in the per-user app data folder.
/// </summary>
public sealed class AppConfig
{
    public int ConfigVersion { get; set; } = 2;

    public List<Rule> Rules { get; set; } = new();

    /// <summary>Folders under review-first management (the simple archiving model).
    /// Independent of <see cref="Rules"/>; both are evaluated each sweep.</summary>
    public List<ManagedFolder> ManagedFolders { get; set; } = new();

    // Preferences ------------------------------------------------------------

    /// <summary>Periodic sweep interval; a safety net behind real-time watching.</summary>
    public int SweepInterval { get; set; } = 5;
    public TimeUnit SweepUnit { get; set; } = TimeUnit.Minutes;

    /// <summary>React to file-system events in real time (FileSystemWatcher).</summary>
    public bool RealTimeWatch { get; set; } = true;

    public bool EnableLogging { get; set; } = true;

    /// <summary>Show a Windows toast when an action is taken.</summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>Confirm before exiting from the tray/menu.</summary>
    public bool ConfirmExit { get; set; } = true;

    /// <summary>Launch automatically at Windows sign-in.</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>Remembered folder for file/folder pickers.</summary>
    public string LastFolder { get; set; } = string.Empty;

    /// <summary>Whether we've already offered to convert a simple archive rule into
    /// a managed folder. Offered once (accepted or not), so upgraders aren't nagged.</summary>
    public bool ArchiveConversionOffered { get; set; }
}

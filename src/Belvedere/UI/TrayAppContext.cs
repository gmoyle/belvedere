using Belvedere.Engine;
using Belvedere.Models;
using Belvedere.Services;

namespace Belvedere.UI;

/// <summary>
/// The application's root. Lives in the system tray, owns the config, the
/// processor and the watch service, and marshals background notifications and
/// confirmation prompts onto the UI thread.
/// </summary>
public sealed class TrayAppContext : ApplicationContext
{
    private readonly ConfigStore _store;
    private readonly Logger _log;
    private readonly RuleProcessor _processor;
    private readonly WatchService _watcher;

    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Control _marshal; // hidden control for UI-thread Invoke

    private readonly Icon _iconActive;
    private readonly Icon _iconPaused;

    private AppConfig _config;
    private MainForm? _mainForm;

    public TrayAppContext(ConfigStore store, Logger log, AppConfig config)
    {
        _store = store;
        _log = log;
        _config = config;

        // The HKCU Run key is the ground truth for "launch at sign-in": the
        // installer can write it directly, so adopt what it actually says
        // instead of trusting the persisted flag. Otherwise a fresh install's
        // registration would be silently undone the first time settings are
        // saved (ApplyConfig calls SetEnabled with the persisted value).
        try { _config.RunAtStartup = StartupManager.IsEnabled(); }
        catch (Exception ex) { _log.Error("Could not read startup registration: " + ex.Message); }

        _marshal = new Control();
        _ = _marshal.Handle; // force handle creation so Invoke works

        _iconActive = AppIcons.Active;
        _iconPaused = AppIcons.Paused;

        _processor = new RuleProcessor(_log)
        {
            OnActionNotify = msg => Post(() => Notify("Action taken", msg)),
            OnErrorNotify = msg => Post(() => Notify("Belvedere error", msg, ToolTipIcon.Error)),
            ConfirmPrompt = Confirm,
        };

        _watcher = new WatchService(_log, _processor)
        {
            OnWatchError = msg => Post(() => Notify("Belvedere error", msg, ToolTipIcon.Error)),
        };

        _pauseItem = new ToolStripMenuItem("&Pause", null, (_, _) => TogglePause());
        _startupItem = new ToolStripMenuItem("Start with &Windows", null, (_, _) => ToggleStartup())
        {
            Checked = _config.RunAtStartup,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("&Manage…", null, (_, _) => ShowManage()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripMenuItem("&Run now", null, (_, _) => _watcher.RunNow()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("&About", null, (_, _) => ShowAbout()));
        menu.Items.Add(new ToolStripMenuItem("E&xit", null, (_, _) => ExitApp()));

        _tray = new NotifyIcon
        {
            Icon = _iconActive,
            Text = $"Belvedere {AppInfo.Version}",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowManage();

        _watcher.Start(_config);
    }


    // -- Menu actions --------------------------------------------------------

    private void ShowManage()
    {
        if (_mainForm is null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(_store, _log, _config, ApplyConfig);
        }

        if (!_mainForm.Visible) _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    /// <summary>Called by MainForm when the user saves changes. Returns whether
    /// the save actually succeeded, so the caller can decide whether it's safe
    /// to close (a failed save must not look like a successful one).</summary>
    private bool ApplyConfig(AppConfig updated)
    {
        try
        {
            _store.Save(updated);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save settings: " + ex.Message);
            MessageBox.Show(
                $"Belvedere could not save your settings:\n{ex.Message}\n\n" +
                "Your changes have not been applied. Please try again.",
                "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _config = updated;
        _log.Enabled = updated.EnableLogging;
        _watcher.Reload(updated);

        // Non-fatal: settings are saved either way, this is just the registry
        // "launch at sign-in" toggle, which corporate policy can legitimately block.
        try
        {
            StartupManager.SetEnabled(updated.RunAtStartup);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to update startup registration: " + ex.Message);
            MessageBox.Show(
                $"Your settings were saved, but Belvedere could not update the " +
                $"\"start at sign-in\" registration:\n{ex.Message}",
                "Startup setting not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        return true;
    }

    private void TogglePause()
    {
        bool pause = !_watcher.IsPaused;
        _watcher.SetPaused(pause);
        _pauseItem.Checked = pause;
        _tray.Icon = pause ? _iconPaused : _iconActive;
        _tray.Text = pause ? "Belvedere (paused)" : $"Belvedere {AppInfo.Version}";
    }

    private void ToggleStartup()
    {
        bool enable = !_startupItem.Checked;

        try
        {
            StartupManager.SetEnabled(enable);
        }
        catch (Exception ex)
        {
            // Corporate policy can legitimately block writing the Run key.
            _log.Error("Failed to update startup registration: " + ex.Message);
            MessageBox.Show(
                $"Belvedere could not update the \"start with Windows\" setting:\n{ex.Message}",
                "Startup setting not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _startupItem.Checked = enable;
        _config.RunAtStartup = enable;

        // Persist so the flag survives a restart and matches the registry.
        try { _store.Save(_config); }
        catch (Exception ex) { _log.Error("Failed to save startup preference: " + ex.Message); }

        // Keep an open Manage window's checkbox in sync so its next save
        // doesn't revert this change.
        if (_mainForm is { IsDisposed: false })
            _mainForm.SyncStartupState(enable);

        _log.Info(enable ? "Enabled start with Windows." : "Disabled start with Windows.");
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            $"Belvedere {AppInfo.Version}\n\n" +
            "Automated file manager for Windows.\n" +
            "Revived as a native .NET application from Adam Pash's\n" +
            "original AutoHotkey app distributed by Lifehacker.\n\n" +
            "Licensed under the GNU GPL v3.",
            "About Belvedere", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExitApp()
    {
        if (_config.ConfirmExit)
        {
            var r = MessageBox.Show("Are you sure you want to exit Belvedere?",
                "Exit", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
        }

        _log.Info("Belvedere is closing. Good-bye!");
        _tray.Visible = false;
        _watcher.Dispose();
        _tray.Dispose();
        _marshal.Dispose();
        ExitThread();
    }

    // -- UI-thread marshaling ------------------------------------------------

    private void Post(Action action)
    {
        if (_marshal.IsDisposed) return;
        try { _marshal.BeginInvoke(action); } catch { }
    }

    private bool Confirm(Rule rule, FileSystemInfo entry)
    {
        if (_marshal.IsDisposed) return false;
        return (bool)_marshal.Invoke(new Func<bool>(() =>
            MessageBox.Show(
                $"Are you sure you want to {rule.Action.Label().ToLowerInvariant()} \"{entry.Name}\"\n" +
                $"because of rule \"{rule.Name}\"?",
                "Action Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            == DialogResult.Yes));
    }

    private void Notify(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (!_config.ShowNotifications) return;
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = message;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(4000);
    }
}

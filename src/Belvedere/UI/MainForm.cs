using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Belvedere.Models;
using Belvedere.Services;

namespace Belvedere.UI;

/// <summary>
/// The management window: edit rules, set preferences, and view the log.
/// Works on a private copy of the config; "Save" hands it back to the tray.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ConfigStore _store;
    private readonly Logger _log;
    private readonly Func<AppConfig, bool> _apply;
    private AppConfig _working;

    private readonly ListView _ruleList = new();
    private TextBox _logBox = null!;

    // Preference controls
    private NumericUpDown _sweepValue = null!;
    private ComboBox _sweepUnit = null!;
    private CheckBox _realTime = null!;
    private CheckBox _logging = null!;
    private CheckBox _notifications = null!;
    private CheckBox _confirmExit = null!;
    private CheckBox _startup = null!;

    public MainForm(ConfigStore store, Logger log, AppConfig config, Func<AppConfig, bool> apply)
    {
        _store = store;
        _log = log;
        _apply = apply;
        _working = Clone(config);

        Text = "Belvedere — Manage";
        Size = new Size(720, 520);
        MinimumSize = new Size(600, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcons.Active;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildRulesTab());
        tabs.TabPages.Add(BuildPrefsTab());
        tabs.TabPages.Add(BuildLogTab());
        tabs.TabPages.Add(BuildAboutTab());

        var buttons = BuildButtonBar();

        Controls.Add(tabs);
        Controls.Add(buttons);

        _log.Logged += OnLogged;
        FormClosed += (_, _) => _log.Logged -= OnLogged;

        RefreshRuleList();
        LoadPrefsIntoControls();
        RefreshLog();
    }

    // -- Rules tab -----------------------------------------------------------

    private TabPage BuildRulesTab()
    {
        var page = new TabPage("Rules");

        _ruleList.View = View.Details;
        _ruleList.FullRowSelect = true;
        _ruleList.GridLines = true;
        _ruleList.CheckBoxes = true;
        _ruleList.Dock = DockStyle.Fill;
        _ruleList.Columns.Add("Rule", 160);
        _ruleList.Columns.Add("Source folder", 200);
        _ruleList.Columns.Add("Action", 130);
        _ruleList.Columns.Add("Destination", 180);
        _ruleList.DoubleClick += (_, _) => EditSelected();
        _ruleList.ItemChecked += (_, e) =>
        {
            if (e.Item.Tag is Rule r) r.Enabled = e.Item.Checked;
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(4) };
        bar.Controls.Add(MakeButton("Add…", AddRule));
        bar.Controls.Add(MakeButton("Edit…", EditSelected));
        bar.Controls.Add(MakeButton("Duplicate", DuplicateSelected));
        bar.Controls.Add(MakeButton("Delete", DeleteSelected));
        bar.Controls.Add(MakeButton("Import rules.ini…", ImportLegacy));

        page.Controls.Add(_ruleList);
        page.Controls.Add(bar);
        return page;
    }

    private void RefreshRuleList()
    {
        _ruleList.BeginUpdate();
        _ruleList.Items.Clear();
        foreach (var r in _working.Rules)
        {
            var item = new ListViewItem(r.Name) { Checked = r.Enabled, Tag = r };
            item.SubItems.Add(r.SourceFolder);
            item.SubItems.Add(r.Action.Label());
            item.SubItems.Add(r.Destination);
            _ruleList.Items.Add(item);
        }
        _ruleList.EndUpdate();
    }

    private Rule? SelectedRule() =>
        _ruleList.SelectedItems.Count > 0 ? _ruleList.SelectedItems[0].Tag as Rule : null;

    private void AddRule()
    {
        var rule = new Rule();
        using var editor = new RuleEditorForm(rule);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            _working.Rules.Add(rule);
            RefreshRuleList();
        }
    }

    private void EditSelected()
    {
        var rule = SelectedRule();
        if (rule is null) return;
        using var editor = new RuleEditorForm(rule);
        if (editor.ShowDialog(this) == DialogResult.OK)
            RefreshRuleList();
    }

    private void DuplicateSelected()
    {
        var rule = SelectedRule();
        if (rule is null) return;
        var copy = Clone(rule);
        copy.Name = rule.Name + " (copy)";
        _working.Rules.Add(copy);
        RefreshRuleList();
    }

    private void DeleteSelected()
    {
        var rule = SelectedRule();
        if (rule is null) return;
        if (MessageBox.Show($"Delete rule \"{rule.Name}\"?", "Delete",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _working.Rules.Remove(rule);
        RefreshRuleList();
    }

    private void ImportLegacy()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Import legacy rules.ini",
            Filter = "Belvedere settings (*.ini)|*.ini|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var imported = IniImporter.Import(dlg.FileName);
            if (imported.Config.Rules.Count == 0)
            {
                MessageBox.Show("No rules were found in that file.", "Import",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _working.Rules.AddRange(imported.Config.Rules);
            RefreshRuleList();
            MessageBox.Show(IniImporter.FormatSummary(imported), "Import",
                MessageBoxButtons.OK,
                imported.Warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not import that file:\n{ex.Message}", "Import error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // -- Preferences tab -----------------------------------------------------

    private TabPage BuildPrefsTab()
    {
        var page = new TabPage("Preferences");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _sweepValue = new NumericUpDown { Minimum = 1, Maximum = 100000, Width = 80 };
        _sweepUnit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _sweepUnit.Items.AddRange(Enum.GetNames<TimeUnit>());
        var sweepPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        sweepPanel.Controls.Add(_sweepValue);
        sweepPanel.Controls.Add(_sweepUnit);

        _realTime = new CheckBox { Text = "Watch folders in real time (react instantly to new files)", AutoSize = true };
        _logging = new CheckBox { Text = "Enable logging", AutoSize = true };
        _notifications = new CheckBox { Text = "Show a notification when an action is taken", AutoSize = true };
        _confirmExit = new CheckBox { Text = "Confirm before exiting", AutoSize = true };
        _startup = new CheckBox { Text = "Start Belvedere when I sign in to Windows", AutoSize = true };

        layout.Controls.Add(new Label { Text = "Sweep folders every:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(sweepPanel, 1, 0);
        layout.Controls.Add(_realTime, 1, 1);
        layout.Controls.Add(_logging, 1, 2);
        layout.Controls.Add(_notifications, 1, 3);
        layout.Controls.Add(_confirmExit, 1, 4);
        layout.Controls.Add(_startup, 1, 5);

        page.Controls.Add(layout);
        return page;
    }

    private void LoadPrefsIntoControls()
    {
        _sweepValue.Value = Math.Clamp(_working.SweepInterval, 1, 100000);
        _sweepUnit.SelectedItem = _working.SweepUnit.ToString();
        _realTime.Checked = _working.RealTimeWatch;
        _logging.Checked = _working.EnableLogging;
        _notifications.Checked = _working.ShowNotifications;
        _confirmExit.Checked = _working.ConfirmExit;
        _startup.Checked = _working.RunAtStartup;
    }

    private void SavePrefsFromControls()
    {
        _working.SweepInterval = (int)_sweepValue.Value;
        if (Enum.TryParse<TimeUnit>(_sweepUnit.SelectedItem?.ToString(), out var u)) _working.SweepUnit = u;
        _working.RealTimeWatch = _realTime.Checked;
        _working.EnableLogging = _logging.Checked;
        _working.ShowNotifications = _notifications.Checked;
        _working.ConfirmExit = _confirmExit.Checked;
        _working.RunAtStartup = _startup.Checked;
    }

    // -- Log tab -------------------------------------------------------------

    private TabPage BuildLogTab()
    {
        var page = new TabPage("Log");
        _logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 8.5f),
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(4) };
        bar.Controls.Add(MakeButton("Refresh", RefreshLog));
        bar.Controls.Add(MakeButton("Clear", () => { _log.Clear(); RefreshLog(); }));
        bar.Controls.Add(MakeButton("Save…", SaveLog));

        page.Controls.Add(_logBox);
        page.Controls.Add(bar);
        return page;
    }

    // -- About tab -------------------------------------------------------------

    private TabPage BuildAboutTab()
    {
        var page = new TabPage("About");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(20),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var icon = new PictureBox
        {
            Image = AppIcons.Active.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Width = 72,
            Height = 72,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 0),
        };

        var title = new Label
        {
            Text = $"Belvedere {AppInfo.Version}",
            Font = new Font(Font.FontFamily, 15f, FontStyle.Bold),
            AutoSize = true,
        };

        var description = new Label
        {
            Text = "Automated file manager for Windows. Watches folders and moves,\n" +
                   "copies, renames, recycles, or otherwise acts on files automatically,\n" +
                   "based on rules you define.",
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 12),
        };

        var credits = new Label
        {
            Text = "Originally written by Adam Pash, distributed by Lifehacker.\n" +
                   "Maintained by Matthew Shorts. Icon design by What Cheer.\n\n" +
                   "Revived as a native .NET application, licensed under the GNU GPL v3.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        var link = new LinkLabel { Text = "View source on GitHub", AutoSize = true };
        link.LinkClicked += (_, _) => OpenGitHubRepo();

        var textPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
        };
        textPanel.Controls.Add(title);
        textPanel.Controls.Add(description);
        textPanel.Controls.Add(credits);
        textPanel.Controls.Add(link);

        layout.Controls.Add(icon, 0, 0);
        layout.Controls.Add(textPanel, 1, 0);

        page.Controls.Add(layout);
        return page;
    }

    private static void OpenGitHubRepo()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/gmoyle/belvedere") { UseShellExecute = true });
        }
        catch
        {
            // Opening a browser link is a convenience, not critical - fail quietly.
        }
    }

    private void RefreshLog()
    {
        _logBox.Text = _log.ReadAll();
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void OnLogged(object? sender, LogEntry e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                _logBox.AppendText(e + Environment.NewLine);
            });
        }
        catch { }
    }

    private void SaveLog()
    {
        using var dlg = new SaveFileDialog { Filter = "Log file (*.log)|*.log|Text (*.txt)|*.txt", FileName = "belvedere.log" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dlg.FileName, _log.ReadAll());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the log:\n{ex.Message}", "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // -- Bottom buttons ------------------------------------------------------

    private FlowLayoutPanel BuildButtonBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(8),
        };
        // Only close if the save actually succeeded - a failed save must never
        // look the same as a successful one (see TrayAppContext.ApplyConfig).
        bar.Controls.Add(MakeButton("Save && Close", () => { if (SaveAll()) Close(); }, primary: true));
        bar.Controls.Add(MakeButton("Apply", () => SaveAll()));
        bar.Controls.Add(MakeButton("Close", Close));
        return bar;
    }

    private bool SaveAll()
    {
        SavePrefsFromControls();
        return _apply(Clone(_working)); // hand a fresh copy to the tray/watcher
    }

    // -- Helpers -------------------------------------------------------------

    private static Button MakeButton(string text, Action onClick, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        if (primary) b.Font = new Font(b.Font, FontStyle.Bold);
        b.Click += (_, _) => onClick();
        return b;
    }

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CloneOptions), CloneOptions)!;
}

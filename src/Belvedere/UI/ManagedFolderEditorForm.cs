using Belvedere.Models;

namespace Belvedere.UI;

/// <summary>
/// Edits a single <see cref="ManagedFolder"/> - the simple archiving setup: one
/// folder, one age, one destination. Learned Keep/Snooze decisions are shown
/// read-only, with a Remove button so a mis-click in review can be undone here.
/// Changes are written back to the passed object only when the user clicks OK.
/// </summary>
public sealed class ManagedFolderEditorForm : Form
{
    private readonly ManagedFolder _folder;

    private readonly TextBox _path = new() { Width = 360 };
    private readonly TextBox _destination = new() { Width = 360 };
    private readonly NumericUpDown _age = new() { Minimum = 1, Maximum = 100000, Width = 80 };
    private readonly ComboBox _unit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _basis = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ListView _decisions = new();

    private const string BasisUsed = "Last used (opened or edited)";
    private const string BasisModified = "Last modified";

    public ManagedFolderEditorForm(ManagedFolder folder)
    {
        _folder = folder;

        Text = "Managed folder";
        Size = new Size(640, 520);
        MinimumSize = new Size(560, 460);
        StartPosition = FormStartPosition.CenterParent;
        Icon = AppIcons.Active;

        Controls.Add(BuildLayout());
        LoadFromFolder();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // settings
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // decisions header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // decisions list
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons

        root.Controls.Add(BuildSettings(), 0, 0);
        root.Controls.Add(new Label
        {
            Text = "Things you've chosen to keep or snooze:",
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 2),
        }, 0, 1);
        root.Controls.Add(BuildDecisions(), 0, 2);
        root.Controls.Add(BuildButtons(), 0, 3);
        return root;
    }

    private Control BuildSettings()
    {
        var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Top };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        t.Controls.Add(new Label { Text = "Watch folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        t.Controls.Add(WithBrowse(_path, BrowsePath), 1, 0);

        t.Controls.Add(new Label { Text = "Archive to:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        t.Controls.Add(WithBrowse(_destination, BrowseDestination), 1, 1);

        _unit.Items.AddRange(Enum.GetNames<TimeUnit>());
        var agePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        agePanel.Controls.Add(_age);
        agePanel.Controls.Add(_unit);
        t.Controls.Add(new Label { Text = "Archive when unused for:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        t.Controls.Add(agePanel, 1, 2);

        _basis.Items.AddRange(new object[] { BasisUsed, BasisModified });
        t.Controls.Add(new Label { Text = "\"Unused\" means:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        t.Controls.Add(_basis, 1, 3);

        return t;
    }

    private static Control WithBrowse(TextBox box, Action onBrowse)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => onBrowse();
        panel.Controls.Add(box);
        panel.Controls.Add(browse);
        return panel;
    }

    private Control BuildDecisions()
    {
        _decisions.View = View.Details;
        _decisions.FullRowSelect = true;
        _decisions.GridLines = true;
        _decisions.Dock = DockStyle.Fill;
        _decisions.Columns.Add("Decision", 90);
        _decisions.Columns.Add("Applies to", 220);
        _decisions.Columns.Add("Match", 110);
        _decisions.Columns.Add("Until", 140);

        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(_decisions);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
        var remove = new Button { Text = "Remove selected", AutoSize = true };
        remove.Click += (_, _) => RemoveSelectedDecision();
        bar.Controls.Add(remove);

        var box = new GroupBox { Dock = DockStyle.Fill, Text = string.Empty };
        box.Controls.Add(host);
        box.Controls.Add(bar);
        return box;
    }

    private Control BuildButtons()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
        var ok = new Button { Text = "OK", AutoSize = true };
        ok.Click += (_, _) => OnOk();
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        bar.Controls.Add(ok);
        bar.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        return bar;
    }

    // -- Load / save ---------------------------------------------------------

    private void LoadFromFolder()
    {
        _path.Text = _folder.Path;
        _destination.Text = _folder.Destination;
        _age.Value = Math.Clamp(_folder.MaxIdleAge, 1, 100000);
        _unit.SelectedItem = _folder.MaxIdleUnit.ToString();
        if (_unit.SelectedIndex < 0) _unit.SelectedItem = TimeUnit.Weeks.ToString();
        _basis.SelectedItem = _folder.Basis == IdleBasis.Used ? BasisUsed : BasisModified;
        RefreshDecisions();
    }

    private void RefreshDecisions()
    {
        _decisions.BeginUpdate();
        _decisions.Items.Clear();
        foreach (var d in _folder.Decisions)
        {
            var item = new ListViewItem(d.Kind == DecisionKind.KeepForever ? "Keep" : "Snooze") { Tag = d };
            item.SubItems.Add(d.Scope == DecisionScope.Extension ? "." + d.Target : d.Target);
            item.SubItems.Add(d.Scope.ToString());
            item.SubItems.Add(d.SnoozeUntil?.ToString("yyyy-MM-dd") ?? "—");
            _decisions.Items.Add(item);
        }
        _decisions.EndUpdate();
    }

    private void RemoveSelectedDecision()
    {
        if (_decisions.SelectedItems.Count == 0) return;
        if (_decisions.SelectedItems[0].Tag is ArchiveDecision d)
        {
            _folder.Decisions.Remove(d);
            RefreshDecisions();
        }
    }

    private void OnOk()
    {
        if (string.IsNullOrWhiteSpace(_path.Text))
        {
            Warn("Please choose a folder to watch.", _path);
            return;
        }
        if (string.IsNullOrWhiteSpace(_destination.Text))
        {
            Warn("Please choose a folder to archive things to.", _destination);
            return;
        }
        if (PathEquals(_path.Text, _destination.Text))
        {
            Warn("The archive folder must be different from the watched folder.", _destination);
            return;
        }

        _folder.Path = _path.Text.Trim().TrimEnd('\\');
        _folder.Destination = _destination.Text.Trim().TrimEnd('\\');
        _folder.MaxIdleAge = (int)_age.Value;
        if (Enum.TryParse<TimeUnit>(_unit.SelectedItem?.ToString(), out var u)) _folder.MaxIdleUnit = u;
        _folder.Basis = Equals(_basis.SelectedItem, BasisModified) ? IdleBasis.Modified : IdleBasis.Used;

        DialogResult = DialogResult.OK;
        Close();
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('\\'), b.Trim().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private void Warn(string message, Control focus)
    {
        MessageBox.Show(message, "Managed folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
    }

    private void BrowsePath()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose the folder to keep tidy" };
        if (!string.IsNullOrWhiteSpace(_path.Text)) dlg.SelectedPath = _path.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.SelectedPath;
    }

    private void BrowseDestination()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose where archived items go" };
        if (!string.IsNullOrWhiteSpace(_destination.Text)) dlg.SelectedPath = _destination.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) _destination.Text = dlg.SelectedPath;
    }
}

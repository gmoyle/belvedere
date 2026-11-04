using Belvedere.Engine;
using Belvedere.Models;

namespace Belvedere.UI;

/// <summary>
/// The archive review: everything a managed folder currently proposes to archive,
/// shown once, for the user to approve. Nothing moves until the user acts here.
/// Checking a whole (all-stale) subfolder archives it as a unit; checking
/// individual files archives just those. "Keep" and "Snooze" record standing
/// decisions so the item isn't proposed again - this is how the folder's rules get
/// written, by reviewing rather than up front. Every archive is undoable.
/// </summary>
public sealed class ArchiveReviewForm : Form
{
    private readonly IReadOnlyList<ManagedFolder> _folders;
    private readonly ArchiveJournal _journal;
    private readonly Func<bool> _saveConfig;

    private readonly ComboBox _folderPick = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly Label _summary = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 10, 10, 0) };
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, CheckBoxes = true, HideSelection = false };
    private readonly Button _undo = new() { Text = "Undo last archive", AutoSize = true };

    private ManagedFolder _current;
    private ArchivePlan _plan;
    private bool _suppressCascade;

    public ArchiveReviewForm(IReadOnlyList<ManagedFolder> folders, ArchiveJournal journal, Func<bool> saveConfig, ManagedFolder? initial = null)
    {
        _folders = folders;
        _journal = journal;
        _saveConfig = saveConfig;
        _current = initial ?? folders.FirstOrDefault() ?? new ManagedFolder();
        _plan = ArchivePlan.Empty(_current);

        Text = "Review archives";
        Size = new Size(820, 560);
        MinimumSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcons.Active;

        Controls.Add(BuildBody());
        Controls.Add(BuildActionBar());
        Controls.Add(BuildFolderBar());
        Controls.Add(_summary);

        _tree.AfterCheck += OnAfterCheck;

        PopulateFolderPick();
        Rescan();
    }

    // -- Layout --------------------------------------------------------------

    private Control BuildFolderBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 4, 8, 0) };
        bar.Controls.Add(new Label { Text = "Folder:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        bar.Controls.Add(_folderPick);
        _folderPick.SelectedIndexChanged += (_, _) =>
        {
            if (_folderPick.SelectedIndex >= 0 && _folderPick.SelectedIndex < _folders.Count)
            {
                _current = _folders[_folderPick.SelectedIndex];
                Rescan();
            }
        };
        return bar;
    }

    private Control BuildBody() => _tree;

    private Control BuildActionBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8), FlowDirection = FlowDirection.LeftToRight };

        bar.Controls.Add(MakeButton("Archive checked", () => ArchiveChecked()));
        bar.Controls.Add(MakeButton("Keep checked", () => KeepChecked()));

        var snooze = new Button { Text = "Snooze checked ▾", AutoSize = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("2 weeks", null, (_, _) => SnoozeChecked(TimeSpan.FromDays(14)));
        menu.Items.Add("4 weeks", null, (_, _) => SnoozeChecked(TimeSpan.FromDays(28)));
        menu.Items.Add("12 weeks", null, (_, _) => SnoozeChecked(TimeSpan.FromDays(84)));
        snooze.Click += (_, _) => menu.Show(snooze, new Point(0, snooze.Height));
        bar.Controls.Add(snooze);

        bar.Controls.Add(new Label { Width = 20 }); // spacer
        bar.Controls.Add(MakeButton("Check all", () => SetAllChecked(true)));
        bar.Controls.Add(MakeButton("Check none", () => SetAllChecked(false)));

        bar.Controls.Add(new Label { Width = 20 });
        _undo.Click += (_, _) => UndoLast();
        bar.Controls.Add(_undo);

        bar.Controls.Add(MakeButton("Close", Close));
        return bar;
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
        b.Click += (_, _) => onClick();
        return b;
    }

    // -- Populate ------------------------------------------------------------

    private void PopulateFolderPick()
    {
        _folderPick.Items.Clear();
        foreach (var f in _folders) _folderPick.Items.Add(f.Path);
        int idx = _folders.ToList().IndexOf(_current);
        if (idx >= 0) _folderPick.SelectedIndex = idx;

        // Only worth showing the picker when there's more than one folder.
        bool multiple = _folders.Count > 1;
        _folderPick.Visible = multiple;
        _folderPick.Parent!.Visible = multiple;
    }

    private void Rescan()
    {
        _plan = ArchiveScanner.Scan(_current);
        ReloadTree();
        _undo.Enabled = _journal.HasEntries;
    }

    private void ReloadTree()
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();

        foreach (var g in _plan.Groups)
        {
            string header = g.SubfolderPath is null
                ? $"Loose files ({g.Proposals.Count})"
                : g.AllStale
                    ? $"{g.Label}  —  {g.Proposals.Count} file(s), all unused · check to archive whole folder"
                    : $"{g.Label}  —  {g.Proposals.Count} unused file(s)";

            var groupNode = new TreeNode(header) { Tag = g };
            foreach (var p in g.Proposals)
                groupNode.Nodes.Add(new TreeNode(RowText(p)) { Tag = p });
            _tree.Nodes.Add(groupNode);
        }

        _tree.ExpandAll();
        _tree.EndUpdate();

        _summary.Text = _plan.IsEmpty
            ? $"Nothing to archive in {_current.Path} right now."
            : $"{_plan.TotalProposals} item(s) proposed in {_plan.Groups.Count} group(s). " +
              "Check what to archive, then choose an action. Unchecked items stay put.";
    }

    private static string RowText(ArchiveProposal p)
    {
        string size = p.Kind == ProposalKind.Folder ? "folder" : FormatSize(p.SizeBytes);
        return $"{p.Entry.Name}   ·   unused {FormatAge(p.LastUsed)}   ·   {size}";
    }

    // -- Actions -------------------------------------------------------------

    private void ArchiveChecked()
    {
        var approved = new List<ArchiveProposal>();
        var collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (node, g) in GroupNodes())
        {
            if (g.SubfolderPath is not null && g.AllStale && node.Checked && AllChildrenChecked(node))
            {
                var fp = ArchiveScanner.FolderProposalFor(_current, g);
                if (fp is not null) { approved.Add(fp); collapsed.Add(g.SubfolderPath); }
            }
        }

        foreach (var (node, p) in CheckedFileNodes())
        {
            if (node.Parent?.Tag is ProposalGroup pg && pg.SubfolderPath is not null && collapsed.Contains(pg.SubfolderPath))
                continue;
            approved.Add(p);
        }

        if (approved.Count == 0) { Info("Check the items you want to archive first."); return; }

        var result = ArchiveApplier.Apply(_current, approved, _journal);
        _saveConfig();

        if (result.Failed > 0)
            MessageBox.Show(
                $"Archived {result.Total} item(s); {result.Failed} could not be moved:\n\n" +
                string.Join("\n", result.Errors.Take(10)),
                "Archive", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        Rescan();
    }

    private void KeepChecked() => RecordDecisions(DecisionKind.KeepForever, null);

    private void SnoozeChecked(TimeSpan span) => RecordDecisions(DecisionKind.SnoozeUntil, DateTime.Now + span);

    /// <summary>Turns the checked nodes into standing decisions: a fully-checked
    /// subfolder becomes one Folder decision, individual files become Path
    /// decisions.</summary>
    private void RecordDecisions(DecisionKind kind, DateTime? until)
    {
        var decisions = new List<ArchiveDecision>();
        var collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (node, g) in GroupNodes())
        {
            if (g.SubfolderPath is not null && node.Checked && AllChildrenChecked(node))
            {
                decisions.Add(new ArchiveDecision { Kind = kind, Scope = DecisionScope.Folder, Target = g.SubfolderPath, SnoozeUntil = until });
                collapsed.Add(g.SubfolderPath);
            }
        }

        foreach (var (node, p) in CheckedFileNodes())
        {
            if (node.Parent?.Tag is ProposalGroup pg && pg.SubfolderPath is not null && collapsed.Contains(pg.SubfolderPath))
                continue;
            decisions.Add(new ArchiveDecision { Kind = kind, Scope = DecisionScope.Path, Target = p.Entry.FullName, SnoozeUntil = until });
        }

        if (decisions.Count == 0) { Info("Check the items you want to keep or snooze first."); return; }

        _current.Decisions.AddRange(decisions);
        _saveConfig();
        Rescan();
    }

    private void UndoLast()
    {
        if (!_journal.HasEntries) return;
        var r = ArchiveApplier.UndoLast(_journal);
        string msg = $"Restored {r.Restored} item(s) to where they came from.";
        if (r.Skipped > 0) msg += $"\n{r.Skipped} could not be restored (the original spot is occupied, or the copy is gone).";
        MessageBox.Show(msg, "Undo archive", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Rescan();
    }

    // -- Tree helpers --------------------------------------------------------

    private IEnumerable<(TreeNode node, ProposalGroup group)> GroupNodes()
    {
        foreach (TreeNode n in _tree.Nodes)
            if (n.Tag is ProposalGroup g) yield return (n, g);
    }

    private IEnumerable<(TreeNode node, ArchiveProposal p)> CheckedFileNodes()
    {
        foreach (TreeNode group in _tree.Nodes)
            foreach (TreeNode child in group.Nodes)
                if (child.Checked && child.Tag is ArchiveProposal p)
                    yield return (child, p);
    }

    private static bool AllChildrenChecked(TreeNode node) =>
        node.Nodes.Count > 0 && node.Nodes.Cast<TreeNode>().All(c => c.Checked);

    private void SetAllChecked(bool check)
    {
        _suppressCascade = true;
        foreach (TreeNode group in _tree.Nodes)
        {
            group.Checked = check;
            foreach (TreeNode child in group.Nodes) child.Checked = check;
        }
        _suppressCascade = false;
    }

    private void OnAfterCheck(object? sender, TreeViewEventArgs e)
    {
        // Only cascade on a real user click, and only parent -> children.
        if (_suppressCascade || e.Action == TreeViewAction.Unknown || e.Node is null || e.Node.Nodes.Count == 0)
            return;
        _suppressCascade = true;
        foreach (TreeNode child in e.Node.Nodes) child.Checked = e.Node.Checked;
        _suppressCascade = false;
    }

    // -- Formatting ----------------------------------------------------------

    private static string FormatAge(DateTime since)
    {
        var d = DateTime.Now - since;
        if (d.TotalDays >= 365) return $"{d.TotalDays / 365:N1} years";
        if (d.TotalDays >= 60) return $"{d.TotalDays / 30:N0} months";
        if (d.TotalDays >= 1) return $"{d.TotalDays:N0} days";
        if (d.TotalHours >= 1) return $"{d.TotalHours:N0} hours";
        return "recently";
    }

    private static string FormatSize(long bytes)
    {
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:N0} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:N1} MB";
        return $"{mb / 1024.0:N2} GB";
    }

    private void Info(string message) =>
        MessageBox.Show(message, "Review archives", MessageBoxButtons.OK, MessageBoxIcon.Information);
}

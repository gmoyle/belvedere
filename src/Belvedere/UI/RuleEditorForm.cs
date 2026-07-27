using Belvedere.Models;

namespace Belvedere.UI;

/// <summary>
/// Edits a single <see cref="Rule"/>. Changes are written back to the passed
/// object only when the user clicks OK.
/// </summary>
public sealed class RuleEditorForm : Form
{
    private readonly Rule _rule;

    private readonly TextBox _name = new() { Width = 260 };
    private readonly TextBox _source = new() { Width = 380 };
    private readonly CheckBox _recursive = new() { Text = "Include subfolders", AutoSize = true };
    private readonly RadioButton _matchAll = new() { Text = "ALL conditions", AutoSize = true, Checked = true };
    private readonly RadioButton _matchAny = new() { Text = "ANY condition", AutoSize = true };

    private readonly FlowLayoutPanel _conditions = new()
    {
        AutoScroll = true,
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
    };
    private Button? _addConditionButton;

    private readonly ComboBox _action = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label _destLabel = new() { Text = "Destination:", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly TextBox _destination = new() { Width = 340 };
    private readonly Button _destBrowse = new() { Text = "Browse…", AutoSize = true };
    private readonly CheckBox _overwrite = new() { Text = "Overwrite if it already exists", AutoSize = true };
    private readonly CheckBox _confirm = new() { Text = "Ask before acting on each file", AutoSize = true };
    private readonly CheckBox _skipReadOnly = new() { Text = "Skip read-only", AutoSize = true };
    private readonly CheckBox _skipHidden = new() { Text = "Skip hidden", AutoSize = true };
    private readonly CheckBox _skipSystem = new() { Text = "Skip system", AutoSize = true };

    public RuleEditorForm(Rule rule)
    {
        _rule = rule;

        Text = "Edit rule";
        Size = new Size(680, 620);
        MinimumSize = new Size(640, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        Icon = AppIcons.Active;

        _action.Items.AddRange(Enum.GetValues<ActionType>().Select(a => (object)a.Label()).ToArray());
        _action.SelectedIndexChanged += (_, _) => UpdateActionUi();
        _destBrowse.Click += (_, _) => BrowseDestination();

        Controls.Add(BuildLayout());

        LoadFromRule();
        UpdateActionUi();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(10),
            AutoScroll = true,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // header fields
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // match mode
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // conditions (fills)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // action group
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildMatchMode(), 0, 1);
        root.Controls.Add(BuildConditionsGroup(), 0, 2);
        root.Controls.Add(BuildActionGroup(), 0, 3);
        root.Controls.Add(BuildButtons(), 0, 4);
        return root;
    }

    private Control BuildHeader()
    {
        var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Top };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        t.Controls.Add(new Label { Text = "Rule name:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        t.Controls.Add(_name, 1, 0);

        t.Controls.Add(new Label { Text = "Watch folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        var srcPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        var browseSrc = new Button { Text = "Browse…", AutoSize = true };
        browseSrc.Click += (_, _) => BrowseSource();
        srcPanel.Controls.Add(_source);
        srcPanel.Controls.Add(browseSrc);
        t.Controls.Add(srcPanel, 1, 1);
        t.Controls.Add(_recursive, 1, 2);
        return t;
    }

    private Control BuildMatchMode()
    {
        var box = new GroupBox { Text = "Match", AutoSize = true, Dock = DockStyle.Top };
        var f = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Fill };
        f.Controls.Add(new Label { Text = "Match", AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(4, 8, 4, 0) });
        f.Controls.Add(_matchAll);
        f.Controls.Add(_matchAny);
        box.Controls.Add(f);
        return box;
    }

    private Control BuildConditionsGroup()
    {
        var box = new GroupBox { Text = "Conditions", Dock = DockStyle.Fill };
        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(_conditions);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
        _addConditionButton = new Button { Text = "Add condition", AutoSize = true };
        _addConditionButton.Click += (_, _) => AddConditionRow(new Condition());
        bar.Controls.Add(_addConditionButton);

        box.Controls.Add(host);
        box.Controls.Add(bar);
        return box;
    }

    private Control BuildActionGroup()
    {
        var box = new GroupBox { Text = "Action", AutoSize = true, Dock = DockStyle.Top };
        var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        t.Controls.Add(new Label { Text = "Do this:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        t.Controls.Add(_action, 1, 0);

        t.Controls.Add(_destLabel, 0, 1);
        var destPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        destPanel.Controls.Add(_destination);
        destPanel.Controls.Add(_destBrowse);
        t.Controls.Add(destPanel, 1, 1);

        var opts = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        opts.Controls.Add(_overwrite);
        opts.Controls.Add(_confirm);
        t.Controls.Add(opts, 1, 2);

        var skips = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        skips.Controls.Add(new Label { Text = "Ignore files that are:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        skips.Controls.Add(_skipReadOnly);
        skips.Controls.Add(_skipHidden);
        skips.Controls.Add(_skipSystem);
        t.Controls.Add(skips, 1, 3);

        box.Controls.Add(t);
        return box;
    }

    private Control BuildButtons()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.None, AutoSize = true };
        ok.Click += (_, _) => OnOk();
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var test = new Button { Text = "Test rule…", AutoSize = true };
        test.Click += (_, _) => TestRule();
        bar.Controls.Add(ok);
        bar.Controls.Add(cancel);
        bar.Controls.Add(test);
        AcceptButton = ok;
        CancelButton = cancel;
        return bar;
    }

    // -- Load / save ---------------------------------------------------------

    private void LoadFromRule()
    {
        _name.Text = _rule.Name;
        _source.Text = _rule.SourceFolder;
        _recursive.Checked = _rule.Recursive;
        _matchAll.Checked = _rule.Match == MatchMode.All;
        _matchAny.Checked = _rule.Match == MatchMode.Any;
        _action.SelectedItem = _rule.Action.Label();
        _destination.Text = _rule.Destination;
        _overwrite.Checked = _rule.Overwrite;
        _confirm.Checked = _rule.ConfirmAction;
        _skipReadOnly.Checked = _rule.SkipReadOnly;
        _skipHidden.Checked = _rule.SkipHidden;
        _skipSystem.Checked = _rule.SkipSystem;

        if (_rule.Conditions.Count == 0)
            AddConditionRow(new Condition());
        else
            foreach (var c in _rule.Conditions) AddConditionRow(c);
    }

    /// <summary>Validates the current form state and, if valid, returns the
    /// rule it describes - a brand-new object, not yet written into <see
    /// cref="_rule"/>. Shows the relevant warning and returns null if invalid.
    /// Shared by OnOk (which then copies the result into <see cref="_rule"/>)
    /// and the "Test rule" dry run (which never touches <see cref="_rule"/>
    /// at all, so Cancel afterwards still discards everything).</summary>
    private Rule? BuildRuleFromForm()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            MessageBox.Show("Please give the rule a name.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _name.Focus();
            return null;
        }
        if (string.IsNullOrWhiteSpace(_source.Text))
        {
            MessageBox.Show("Please choose a folder to watch.", "Missing folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _source.Focus();
            return null;
        }

        var rows = _conditions.Controls.OfType<ConditionRow>().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show("Add at least one condition.", "No conditions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _addConditionButton?.Focus();
            return null;
        }

        // Validate every condition before accepting any of them - a rule that
        // "saves fine" but silently never matches anything (a bad regex, a
        // non-numeric size/date value) is worse than an upfront error, since
        // nothing at runtime would ever tell the user why it's not working.
        foreach (var row in rows)
        {
            string? error = row.Validate();
            if (error is not null)
            {
                MessageBox.Show(error, "Invalid condition", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _conditions.ScrollControlIntoView(row);
                row.FocusValue();
                return null;
            }
        }

        var conditions = rows.Select(r => r.ToCondition()).ToList();

        var action = Display.ParseAction(_action.SelectedItem?.ToString() ?? "") ?? ActionType.Move;
        if ((action is ActionType.Move or ActionType.MoveLeaveShortcut or ActionType.Copy)
            && string.IsNullOrWhiteSpace(_destination.Text))
        {
            MessageBox.Show("Choose a destination folder for this action.", "Missing destination",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _destination.Focus();
            return null;
        }

        return new Rule
        {
            Name = _name.Text.Trim(),
            SourceFolder = _source.Text.Trim().TrimEnd('\\'),
            Recursive = _recursive.Checked,
            Match = _matchAny.Checked ? MatchMode.Any : MatchMode.All,
            Conditions = conditions,
            Action = action,
            Destination = _destination.Text.Trim(),
            Overwrite = _overwrite.Checked,
            ConfirmAction = _confirm.Checked,
            SkipReadOnly = _skipReadOnly.Checked,
            SkipHidden = _skipHidden.Checked,
            SkipSystem = _skipSystem.Checked,
        };
    }

    private void OnOk()
    {
        var built = BuildRuleFromForm();
        if (built is null) return;

        _rule.Name = built.Name;
        _rule.SourceFolder = built.SourceFolder;
        _rule.Recursive = built.Recursive;
        _rule.Match = built.Match;
        _rule.Conditions = built.Conditions;
        _rule.Action = built.Action;
        _rule.Destination = built.Destination;
        _rule.Overwrite = built.Overwrite;
        _rule.ConfirmAction = built.ConfirmAction;
        _rule.SkipReadOnly = built.SkipReadOnly;
        _rule.SkipHidden = built.SkipHidden;
        _rule.SkipSystem = built.SkipSystem;

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>"Test rule": shows every file the current (possibly unsaved)
    /// form state would act on right now, without acting on any of them.</summary>
    private async void TestRule()
    {
        var built = BuildRuleFromForm();
        if (built is null) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            var result = await Task.Run(() => Belvedere.Engine.DryRunPreview.Run(built));
            using var results = new DryRunResultsForm(built, result);
            results.ShowDialog(this);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    // -- Behavior ------------------------------------------------------------

    private void AddConditionRow(Condition c)
    {
        var row = new ConditionRow(c);
        row.RemoveRequested += (_, _) =>
        {
            _conditions.Controls.Remove(row);
            row.Dispose();
        };
        _conditions.Controls.Add(row);
    }

    private void UpdateActionUi()
    {
        var action = Display.ParseAction(_action.SelectedItem?.ToString() ?? "") ?? ActionType.Move;

        bool usesFolder = action is ActionType.Move or ActionType.MoveLeaveShortcut or ActionType.Copy;
        bool usesRename = action is ActionType.Rename;
        bool usesCommand = action is ActionType.Custom;
        bool hasDest = usesFolder || usesRename || usesCommand;

        _destLabel.Visible = hasDest;
        _destination.Visible = hasDest;
        _destBrowse.Visible = usesFolder || usesCommand;
        _overwrite.Visible = usesFolder || usesRename;

        _destLabel.Text = usesRename ? "Name template:" : usesCommand ? "Program:" : "Destination:";
    }

    private void BrowseSource()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose the folder to watch" };
        if (!string.IsNullOrWhiteSpace(_source.Text)) dlg.SelectedPath = _source.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) _source.Text = dlg.SelectedPath;
    }

    private void BrowseDestination()
    {
        var action = Display.ParseAction(_action.SelectedItem?.ToString() ?? "") ?? ActionType.Move;
        if (action is ActionType.Custom)
        {
            using var dlg = new OpenFileDialog { Title = "Choose a program to run", Filter = "Programs (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _destination.Text = dlg.FileName;
        }
        else
        {
            using var dlg = new FolderBrowserDialog { Description = "Choose the destination folder" };
            if (!string.IsNullOrWhiteSpace(_destination.Text)) dlg.SelectedPath = _destination.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK) _destination.Text = dlg.SelectedPath;
        }
    }

    // -- Condition row -------------------------------------------------------

    /// <summary>One editable condition line: subject / verb / value / unit.</summary>
    private sealed class ConditionRow : FlowLayoutPanel
    {
        private readonly ComboBox _subject = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly ComboBox _verb = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly TextBox _value = new() { Width = 150 };
        private readonly ComboBox _unit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Visible = false };
        private readonly Button _remove = new() { Text = "✕", Width = 28 };

        public event EventHandler? RemoveRequested;

        public ConditionRow(Condition c)
        {
            FlowDirection = FlowDirection.LeftToRight;
            AutoSize = true;
            WrapContents = false;
            Margin = new Padding(2);

            _subject.Items.AddRange(Enum.GetValues<Subject>().Select(s => (object)s.Label()).ToArray());
            _subject.SelectedIndexChanged += (_, _) => OnSubjectChanged();
            _remove.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);

            Controls.Add(_subject);
            Controls.Add(_verb);
            Controls.Add(_value);
            Controls.Add(_unit);
            Controls.Add(_remove);

            // Initialize from the condition.
            _subject.SelectedItem = c.Subject.Label();
            if (_subject.SelectedIndex < 0) _subject.SelectedIndex = 0;
            OnSubjectChanged();
            _verb.SelectedItem = c.Verb.Label();
            if (_verb.SelectedIndex < 0 && _verb.Items.Count > 0) _verb.SelectedIndex = 0;
            _value.Text = c.Value;
            SetUnitFromCondition(c);
        }

        private void OnSubjectChanged()
        {
            var subject = Display.ParseSubject(_subject.SelectedItem?.ToString() ?? "") ?? Subject.Name;

            _verb.Items.Clear();
            foreach (var v in Display.VerbsFor(subject))
                _verb.Items.Add(v.Label());
            if (_verb.Items.Count > 0) _verb.SelectedIndex = 0;

            _unit.Items.Clear();
            if (subject == Subject.Size)
            {
                _unit.Items.AddRange(Enum.GetNames<SizeUnit>());
                _unit.SelectedIndex = (int)SizeUnit.MB;
                _unit.Visible = true;
            }
            else if (subject.IsDateSubject())
            {
                _unit.Items.AddRange(Enum.GetNames<TimeUnit>());
                _unit.SelectedIndex = (int)TimeUnit.Days;
                _unit.Visible = true;
            }
            else
            {
                _unit.Visible = false;
            }
        }

        private void SetUnitFromCondition(Condition c)
        {
            if (c.Subject == Subject.Size) _unit.SelectedItem = c.SizeUnit.ToString();
            else if (c.Subject.IsDateSubject()) _unit.SelectedItem = c.TimeUnit.ToString();
        }

        /// <summary>Puts the cursor on this row's value field, so a validation
        /// error actually leads the user to the thing that needs fixing.</summary>
        public void FocusValue() => _value.Focus();

        /// <summary>Checks this row's value against what its subject/verb
        /// actually require at runtime, so a broken condition is caught here
        /// instead of just silently never matching anything later. Returns a
        /// user-facing error message, or null if the row is valid.</summary>
        public string? Validate()
        {
            var subject = Display.ParseSubject(_subject.SelectedItem?.ToString() ?? "") ?? Subject.Name;
            var verb = Display.ParseVerb(_verb.SelectedItem?.ToString() ?? "") ?? Verb.Is;
            string value = _value.Text.Trim();
            string label = $"\"{subject.Label()} {verb.Label()}\"";

            if (subject == Subject.Size)
            {
                if (!double.TryParse(value, out _))
                    return $"{label} needs a number (got \"{value}\").";
            }
            else if (subject.IsDateSubject())
            {
                if (!double.TryParse(value, out double amount) || amount < 0)
                    return $"{label} needs a non-negative number (got \"{value}\").";
            }

            if (verb == Verb.Regex)
            {
                try { _ = new System.Text.RegularExpressions.Regex(value); }
                catch (ArgumentException ex) { return $"\"{value}\" is not a valid regular expression: {ex.Message}"; }
            }

            return null;
        }

        public Condition ToCondition()
        {
            var subject = Display.ParseSubject(_subject.SelectedItem?.ToString() ?? "") ?? Subject.Name;
            var verb = Display.ParseVerb(_verb.SelectedItem?.ToString() ?? "") ?? Verb.Is;
            var cond = new Condition { Subject = subject, Verb = verb, Value = _value.Text.Trim() };

            if (subject == Subject.Size && Enum.TryParse<SizeUnit>(_unit.SelectedItem?.ToString(), out var su))
                cond.SizeUnit = su;
            else if (subject.IsDateSubject() && Enum.TryParse<TimeUnit>(_unit.SelectedItem?.ToString(), out var tu))
                cond.TimeUnit = tu;

            return cond;
        }
    }
}

using Belvedere.Engine;
using Belvedere.Models;

namespace Belvedere.UI;

/// <summary>
/// Read-only results of a "Test rule" dry run: every file the rule would
/// currently act on, and what would happen to it. Purely informational - this
/// form never touches the filesystem.
/// </summary>
public sealed class DryRunResultsForm : Form
{
    public DryRunResultsForm(Rule rule, PreviewResult result)
    {
        Text = $"Test rule: {rule.Name}";
        Size = new Size(760, 480);
        MinimumSize = new Size(480, 320);
        StartPosition = FormStartPosition.CenterParent;
        Icon = AppIcons.Active;

        var summary = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 8, 10, 0),
            Text = BuildSummaryText(rule, result),
        };

        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
        };
        list.Columns.Add("File", 260);
        list.Columns.Add("Folder", 200);
        list.Columns.Add("Size", 80, HorizontalAlignment.Right);
        list.Columns.Add("Modified", 130);
        list.Columns.Add("Would do", 220);

        foreach (var match in result.Matches)
        {
            var item = new ListViewItem(match.File.Name);
            item.SubItems.Add(match.File.DirectoryName ?? string.Empty);
            item.SubItems.Add(FormatSize(match.File.Length));
            item.SubItems.Add(match.File.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            item.SubItems.Add(match.ActionDescription);
            list.Items.Add(item);
        }

        var closeBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, AutoSize = true };
        closeBar.Controls.Add(close);
        CancelButton = close;
        AcceptButton = close;

        Controls.Add(list);
        Controls.Add(closeBar);
        Controls.Add(summary);
    }

    private static string BuildSummaryText(Rule rule, PreviewResult result)
    {
        if (string.IsNullOrWhiteSpace(rule.SourceFolder) || !Directory.Exists(rule.SourceFolder))
            return $"\"{rule.SourceFolder}\" does not exist - nothing to scan.";

        string text = result.TotalMatched == 0
            ? $"No files currently match, out of {result.FilesScanned:N0} scanned in \"{rule.SourceFolder}\"."
            : $"{result.TotalMatched:N0} file(s) currently match, out of {result.FilesScanned:N0} scanned in \"{rule.SourceFolder}\".";

        if (result.Truncated)
            text += $" Showing the first {result.Matches.Count:N0}.";

        return text;
    }

    private static string FormatSize(long bytes)
    {
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:N0} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:N1} MB";
        return $"{mb / 1024.0:N2} GB";
    }
}

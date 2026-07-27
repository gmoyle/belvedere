using System.Diagnostics;
using System.Text.RegularExpressions;
using Belvedere.Models;
using Microsoft.VisualBasic.FileIO;

namespace Belvedere.Engine;

public enum ActionOutcome { Success, Skipped, Failed }

public sealed record ActionResult(ActionOutcome Outcome, string Message)
{
    public static ActionResult Ok(string msg) => new(ActionOutcome.Success, msg);
    public static ActionResult Skip(string msg) => new(ActionOutcome.Skipped, msg);
    public static ActionResult Fail(string msg) => new(ActionOutcome.Failed, msg);
}

/// <summary>
/// Performs the action a matched rule calls for, on either a file or a
/// folder. All destructive operations go through safe, modern APIs (Recycle
/// Bin via the shell, print/open via the registered verb).
/// </summary>
public static class ActionRunner
{
    public static ActionResult Run(Rule rule, FileSystemInfo entry)
    {
        try
        {
            return entry switch
            {
                DirectoryInfo dir => RunOnDirectory(rule, dir),
                FileInfo file => RunOnFile(rule, file),
                _ => ActionResult.Fail("Unsupported filesystem entry"),
            };
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    private static ActionResult RunOnFile(Rule rule, FileInfo file) => rule.Action switch
    {
        ActionType.Move => MoveFile(rule, file, leaveShortcut: false),
        ActionType.MoveLeaveShortcut => MoveFile(rule, file, leaveShortcut: true),
        ActionType.Copy => CopyFile(rule, file),
        ActionType.Rename => RenameFile(rule, file),
        ActionType.Recycle => RecycleFile(file),
        ActionType.Delete => PermanentlyDeleteFile(file),
        ActionType.Open => OpenEntry(file.FullName),
        ActionType.Print => PrintFile(file),
        ActionType.Custom => CustomEntry(rule, file.FullName, file.DirectoryName ?? string.Empty),
        _ => ActionResult.Fail("Unknown action"),
    };

    private static ActionResult RunOnDirectory(Rule rule, DirectoryInfo dir) => rule.Action switch
    {
        ActionType.Move => MoveDirectory(rule, dir, leaveShortcut: false),
        ActionType.MoveLeaveShortcut => MoveDirectory(rule, dir, leaveShortcut: true),
        ActionType.Copy => CopyDirectory(rule, dir),
        ActionType.Rename => RenameDirectory(rule, dir),
        ActionType.Recycle => RecycleDirectory(dir),
        ActionType.Delete => PermanentlyDeleteDirectory(dir),
        ActionType.Open => OpenEntry(dir.FullName),
        ActionType.Print => ActionResult.Fail("Print is not supported for folders"),
        ActionType.Custom => CustomEntry(rule, dir.FullName, dir.Parent?.FullName ?? string.Empty),
        _ => ActionResult.Fail("Unknown action"),
    };

    /// <summary>Describes, in one line, what running this rule's action would
    /// do to this entry - for the dry-run preview. Pure string logic; never
    /// touches the filesystem or the destination folder.</summary>
    public static string DescribeAction(Rule rule, FileSystemInfo entry)
    {
        string dest = rule.Destination;
        bool isDir = entry is DirectoryInfo;

        return rule.Action switch
        {
            ActionType.Move => $"Move to {Path.Combine(dest, entry.Name)}",
            ActionType.MoveLeaveShortcut => $"Move to {Path.Combine(dest, entry.Name)} (leave shortcut)",
            ActionType.Copy => $"Copy to {Path.Combine(dest, entry.Name)}",
            ActionType.Rename => $"Rename to {ExpandTemplate(dest, entry)}",
            ActionType.Recycle => "Send to Recycle Bin",
            ActionType.Delete => isDir ? "Delete folder permanently (and everything in it)" : "Delete permanently",
            ActionType.Open => "Open",
            ActionType.Print => isDir ? "(Print is not supported for folders)" : "Print",
            ActionType.Custom => $"Run: {dest}",
            _ => "(unknown action)",
        };
    }

    // -- File actions ---------------------------------------------------------

    private static ActionResult MoveFile(Rule rule, FileInfo file, bool leaveShortcut)
    {
        string dest = rule.Destination;
        if (!DestinationFolder.TryEnsure(dest, out var ensureError))
            return ActionResult.Fail(ensureError);

        string target = Path.Combine(dest, file.Name);

        if (File.Exists(target) && !rule.Overwrite)
            return ActionResult.Skip($"Target already exists (overwrite off): {target}");

        // Shortcut must point at the file's NEW location - it's meant to let
        // you find the file from its old spot after it's gone, not point back
        // at a path that's about to stop existing.
        if (leaveShortcut)
            CreateShortcut(target, Path.ChangeExtension(file.FullName, ".lnk"));

        File.Move(file.FullName, target, rule.Overwrite);
        return ActionResult.Ok($"Moved to {target}");
    }

    private static ActionResult CopyFile(Rule rule, FileInfo file)
    {
        string dest = rule.Destination;
        if (!DestinationFolder.TryEnsure(dest, out var ensureError))
            return ActionResult.Fail(ensureError);

        string target = Path.Combine(dest, file.Name);

        if (File.Exists(target) && !rule.Overwrite)
            return ActionResult.Skip($"Target already exists (overwrite off): {target}");

        File.Copy(file.FullName, target, rule.Overwrite);
        return ActionResult.Ok($"Copied to {target}");
    }

    private static ActionResult RenameFile(Rule rule, FileInfo file)
    {
        string newName = ExpandTemplate(rule.Destination, file);
        if (string.IsNullOrWhiteSpace(newName))
            return ActionResult.Fail("Rename template produced an empty name");

        string target = Path.Combine(file.DirectoryName!, newName);
        if (File.Exists(target) && !rule.Overwrite)
            return ActionResult.Skip($"Target already exists (overwrite off): {target}");

        File.Move(file.FullName, target, rule.Overwrite);
        return ActionResult.Ok($"Renamed to {newName}");
    }

    private static ActionResult RecycleFile(FileInfo file)
    {
        FileSystem.DeleteFile(file.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        return ActionResult.Ok("Sent to Recycle Bin");
    }

    private static ActionResult PermanentlyDeleteFile(FileInfo file)
    {
        File.Delete(file.FullName);
        return ActionResult.Ok("Deleted permanently");
    }

    private static ActionResult PrintFile(FileInfo file)
    {
        Process.Start(new ProcessStartInfo(file.FullName) { UseShellExecute = true, Verb = "print" });
        return ActionResult.Ok("Sent to printer");
    }

    // -- Directory actions ----------------------------------------------------

    private static ActionResult MoveDirectory(Rule rule, DirectoryInfo dir, bool leaveShortcut)
    {
        string dest = rule.Destination;
        if (!DestinationFolder.TryEnsure(dest, out var ensureError))
            return ActionResult.Fail(ensureError);

        string target = Path.Combine(dest, dir.Name);

        if (Directory.Exists(target) && !rule.Overwrite)
            return ActionResult.Skip($"Target already exists (overwrite off): {target}");

        if (leaveShortcut)
        {
            string shortcutFolder = dir.Parent?.FullName ?? Path.GetPathRoot(dir.FullName) ?? string.Empty;
            CreateShortcut(target, Path.Combine(shortcutFolder, dir.Name + ".lnk"));
        }

        // FileSystem.MoveDirectory handles overwrite and cross-volume moves
        // (System.IO.Directory.Move does neither).
        FileSystem.MoveDirectory(dir.FullName, target, rule.Overwrite);
        return ActionResult.Ok($"Moved to {target}");
    }

    private static ActionResult CopyDirectory(Rule rule, DirectoryInfo dir)
    {
        string dest = rule.Destination;
        if (!DestinationFolder.TryEnsure(dest, out var ensureError))
            return ActionResult.Fail(ensureError);

        string target = Path.Combine(dest, dir.Name);

        if (Directory.Exists(target) && !rule.Overwrite)
            return ActionResult.Skip($"Target already exists (overwrite off): {target}");

        FileSystem.CopyDirectory(dir.FullName, target, rule.Overwrite);
        return ActionResult.Ok($"Copied to {target}");
    }

    private static ActionResult RenameDirectory(Rule rule, DirectoryInfo dir)
    {
        string newName = ExpandTemplate(rule.Destination, dir);
        if (string.IsNullOrWhiteSpace(newName))
            return ActionResult.Fail("Rename template produced an empty name");

        string parent = dir.Parent?.FullName ?? Path.GetPathRoot(dir.FullName) ?? string.Empty;
        string target = Path.Combine(parent, newName);

        if (Directory.Exists(target))
        {
            if (!rule.Overwrite)
                return ActionResult.Skip($"Target already exists (overwrite off): {target}");

            // Directory.Move has no overwrite option, unlike File.Move; clear
            // the way ourselves when the user has explicitly asked to overwrite.
            Directory.Delete(target, recursive: true);
        }

        Directory.Move(dir.FullName, target);
        return ActionResult.Ok($"Renamed to {newName}");
    }

    private static ActionResult RecycleDirectory(DirectoryInfo dir)
    {
        FileSystem.DeleteDirectory(dir.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        return ActionResult.Ok("Sent to Recycle Bin");
    }

    private static ActionResult PermanentlyDeleteDirectory(DirectoryInfo dir)
    {
        Directory.Delete(dir.FullName, recursive: true);
        return ActionResult.Ok("Deleted permanently");
    }

    // -- Shared (file or folder) actions --------------------------------------

    private static ActionResult OpenEntry(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return ActionResult.Ok("Opened");
    }

    private static ActionResult CustomEntry(Rule rule, string path, string workingDirectoryFallback)
    {
        if (string.IsNullOrWhiteSpace(rule.Destination))
            return ActionResult.Fail("No custom command configured");

        // Destination is the program/command; the entry is passed as an argument.
        var psi = new ProcessStartInfo
        {
            FileName = rule.Destination,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(rule.Destination) ?? workingDirectoryFallback,
        };
        psi.ArgumentList.Add(path);

        Process.Start(psi);
        return ActionResult.Ok($"Ran custom command: {rule.Destination}");
    }

    // -- Helpers -------------------------------------------------------------

    private static readonly char[] IllegalNameChars = Path.GetInvalidFileNameChars();

    private static string ExpandTemplate(string template, FileSystemInfo entry)
    {
        var now = DateTime.Now;
        string nameNoExt = Path.GetFileNameWithoutExtension(entry.Name);
        string ext = entry.Extension; // includes leading dot, matching original ".ext"
        string drive = Path.GetPathRoot(entry.FullName)?.TrimEnd('\\', '/') ?? string.Empty;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["[filename]"] = nameNoExt,
            ["[fullname]"] = entry.Name,
            ["[ext]"] = ext,
            ["[drive]"] = drive,
            ["[YYYY]"] = now.ToString("yyyy"),
            ["[MM]"] = now.ToString("MM"),
            ["[DD]"] = now.ToString("dd"),
            ["[MMMM]"] = now.ToString("MMMM"),
            ["[MMM]"] = now.ToString("MMM"),
            ["[DDDD]"] = now.ToString("dddd"),
            ["[DDD]"] = now.ToString("ddd"),
            ["[WDay]"] = ((int)now.DayOfWeek + 1).ToString(),
            ["[YDay]"] = now.DayOfYear.ToString(),
            ["[hh]"] = now.ToString("HH"),
            ["[mm]"] = now.ToString("mm"),
            ["[ss]"] = now.ToString("ss"),
            ["[ms]"] = now.ToString("fff"),
            ["[DT]"] = now.ToString("yyyyMMddHHmmss"),
            ["[DT-UTC]"] = now.ToUniversalTime().ToString("yyyyMMddHHmmss"),
        };

        string result = template;
        foreach (var (token, replacement) in map)
            result = Regex.Replace(result, Regex.Escape(token), replacement.Replace("$", "$$"), RegexOptions.IgnoreCase);

        // Strip characters illegal in a file name.
        foreach (char bad in IllegalNameChars)
            result = result.Replace(bad.ToString(), string.Empty);

        return result.Trim();
    }

    /// <summary>Creates a .lnk shortcut via the WScript.Shell COM object.</summary>
    private static void CreateShortcut(string targetPath, string shortcutPath)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell is null) return;

        try
        {
            dynamic link = shell.CreateShortcut(shortcutPath);
            link.TargetPath = targetPath;
            link.WorkingDirectory = Path.GetDirectoryName(targetPath);
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}

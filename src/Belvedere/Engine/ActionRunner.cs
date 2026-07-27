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
/// Performs the action a matched rule calls for. All destructive operations go
/// through safe, modern APIs (Recycle Bin via the shell, print/open via the
/// registered verb).
/// </summary>
public static class ActionRunner
{
    public static ActionResult Run(Rule rule, FileInfo file)
    {
        try
        {
            return rule.Action switch
            {
                ActionType.Move => Move(rule, file, leaveShortcut: false),
                ActionType.MoveLeaveShortcut => Move(rule, file, leaveShortcut: true),
                ActionType.Copy => Copy(rule, file),
                ActionType.Rename => Rename(rule, file),
                ActionType.Recycle => Recycle(file),
                ActionType.Delete => Delete(file),
                ActionType.Open => Open(file),
                ActionType.Print => Print(file),
                ActionType.Custom => Custom(rule, file),
                _ => ActionResult.Fail("Unknown action"),
            };
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    private static ActionResult Move(Rule rule, FileInfo file, bool leaveShortcut)
    {
        string dest = rule.Destination;
        if (!TryEnsureDestination(dest, out var ensureError))
            return ActionResult.Fail(ensureError);

        string target = Path.Combine(dest, file.Name);

        if (File.Exists(target) && !rule.Overwrite)
            return ActionResult.Skip($"Target already exists (overwrite off): {target}");

        if (leaveShortcut)
            CreateShortcut(file.FullName, Path.ChangeExtension(file.FullName, ".lnk"));

        File.Move(file.FullName, target, rule.Overwrite);
        return ActionResult.Ok($"Moved to {target}");
    }

    private static ActionResult Copy(Rule rule, FileInfo file)
    {
        string dest = rule.Destination;
        if (!TryEnsureDestination(dest, out var ensureError))
            return ActionResult.Fail(ensureError);

        string target = Path.Combine(dest, file.Name);

        if (File.Exists(target) && !rule.Overwrite)
            return ActionResult.Skip($"Target already exists (overwrite off): {target}");

        File.Copy(file.FullName, target, rule.Overwrite);
        return ActionResult.Ok($"Copied to {target}");
    }

    private static ActionResult Rename(Rule rule, FileInfo file)
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

    private static ActionResult Recycle(FileInfo file)
    {
        FileSystem.DeleteFile(file.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        return ActionResult.Ok("Sent to Recycle Bin");
    }

    private static ActionResult Delete(FileInfo file)
    {
        File.Delete(file.FullName);
        return ActionResult.Ok("Deleted permanently");
    }

    private static ActionResult Open(FileInfo file)
    {
        Process.Start(new ProcessStartInfo(file.FullName) { UseShellExecute = true });
        return ActionResult.Ok("Opened");
    }

    private static ActionResult Print(FileInfo file)
    {
        Process.Start(new ProcessStartInfo(file.FullName) { UseShellExecute = true, Verb = "print" });
        return ActionResult.Ok("Sent to printer");
    }

    private static ActionResult Custom(Rule rule, FileInfo file)
    {
        if (string.IsNullOrWhiteSpace(rule.Destination))
            return ActionResult.Fail("No custom command configured");

        // Destination is the program/command; the file is passed as an argument.
        var psi = new ProcessStartInfo
        {
            FileName = rule.Destination,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(rule.Destination) ?? file.DirectoryName!,
        };
        psi.ArgumentList.Add(file.FullName);

        Process.Start(psi);
        return ActionResult.Ok($"Ran custom command: {rule.Destination}");
    }

    // -- Helpers -------------------------------------------------------------

    private static readonly char[] IllegalNameChars = Path.GetInvalidFileNameChars();

    /// <summary>Creates the destination folder if it doesn't exist yet, rather
    /// than failing the action - the user picked this folder on purpose, it
    /// just may not have been created (or may have been cleaned up) yet.</summary>
    private static bool TryEnsureDestination(string dest, out string error)
    {
        if (string.IsNullOrWhiteSpace(dest))
        {
            error = "No destination folder is configured for this rule.";
            return false;
        }

        if (Directory.Exists(dest))
        {
            error = string.Empty;
            return true;
        }

        try
        {
            Directory.CreateDirectory(dest);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Destination folder does not exist and could not be created: {dest} ({ex.Message})";
            return false;
        }
    }

    private static string ExpandTemplate(string template, FileInfo file)
    {
        var now = DateTime.Now;
        string nameNoExt = Path.GetFileNameWithoutExtension(file.Name);
        string ext = file.Extension; // includes leading dot, matching original ".ext"
        string drive = Path.GetPathRoot(file.FullName)?.TrimEnd('\\', '/') ?? string.Empty;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["[filename]"] = nameNoExt,
            ["[fullname]"] = file.Name,
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

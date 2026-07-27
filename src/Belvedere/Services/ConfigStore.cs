using System.Text.Json;
using System.Text.Json.Serialization;
using Belvedere.Models;

namespace Belvedere.Services;

/// <summary>
/// Loads and saves <see cref="AppConfig"/> as JSON under
/// %AppData%\Belvedere\config.json. Enums are written as readable strings.
/// </summary>
/// <summary>The result of loading config: the config itself, and - if the
/// existing file couldn't be read - a user-facing explanation of what
/// happened and where the unreadable file was backed up.</summary>
public sealed record LoadResult(AppConfig Config, string? Warning);

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string ConfigPath { get; }
    public string DataFolder { get; }

    public ConfigStore(string? dataFolder = null)
    {
        DataFolder = dataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Belvedere");
        Directory.CreateDirectory(DataFolder);
        ConfigPath = Path.Combine(DataFolder, "config.json");
    }

    public LoadResult Load()
    {
        if (!File.Exists(ConfigPath))
            return new LoadResult(new AppConfig(), null);

        try
        {
            string json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, Options);
            if (config is not null)
                return new LoadResult(config, null);

            // Valid JSON but not the shape we expect (e.g. the file is "null").
            return new LoadResult(new AppConfig(), BuildResetWarning(TryBackupCorrupt()));
        }
        catch
        {
            // Corrupt config shouldn't brick the app; back it up and start fresh
            // - but the user needs to know their rules just vanished, and why.
            return new LoadResult(new AppConfig(), BuildResetWarning(TryBackupCorrupt()));
        }
    }

    public void Save(AppConfig config)
    {
        string json = JsonSerializer.Serialize(config, Options);
        // Write-then-replace to avoid a truncated file if we're interrupted.
        string tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    private static string BuildResetWarning(string? backupPath) => backupPath is not null
        ? "Your Belvedere settings could not be read (the file may be corrupted) " +
          "and have been reset to defaults.\n\nThe unreadable file was backed up to:\n" + backupPath
        : "Your Belvedere settings could not be read (the file may be corrupted) " +
          "and have been reset to defaults.";

    private string? TryBackupCorrupt()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            string backupPath = ConfigPath + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(ConfigPath, backupPath, overwrite: true);
            return backupPath;
        }
        catch
        {
            return null; // best effort; the reset warning is still shown either way
        }
    }
}

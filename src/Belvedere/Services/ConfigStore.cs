using System.Text.Json;
using System.Text.Json.Serialization;
using Belvedere.Models;

namespace Belvedere.Services;

/// <summary>
/// Loads and saves <see cref="AppConfig"/> as JSON under
/// %AppData%\Belvedere\config.json. Enums are written as readable strings.
/// </summary>
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

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        }
        catch
        {
            // Corrupt config shouldn't brick the app; back it up and start fresh.
            TryBackupCorrupt();
            return new AppConfig();
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

    private void TryBackupCorrupt()
    {
        try
        {
            if (File.Exists(ConfigPath))
                File.Move(ConfigPath, ConfigPath + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
        }
        catch { /* best effort */ }
    }
}

namespace Belvedere.Engine;

/// <summary>
/// Shared "make sure this destination folder exists" logic, used both when a
/// rule actually runs (<see cref="ActionRunner"/>) and when a rule is imported
/// from a legacy rules.ini (<see cref="Belvedere.Services.IniImporter"/>) so a
/// folder that simply hasn't been created yet doesn't block either path.
/// </summary>
public static class DestinationFolder
{
    public static bool TryEnsure(string path, out string error)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "No destination folder is configured.";
            return false;
        }

        if (Directory.Exists(path))
        {
            error = string.Empty;
            return true;
        }

        try
        {
            Directory.CreateDirectory(path);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Destination folder does not exist and could not be created: {path} ({ex.Message})";
            return false;
        }
    }
}

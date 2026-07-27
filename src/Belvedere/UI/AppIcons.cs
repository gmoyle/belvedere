using System.Reflection;

namespace Belvedere.UI;

/// <summary>
/// Loads the tray/window icons from resources embedded in the executable, so
/// the app remains a single self-contained file with no loose resource folder.
/// </summary>
public static class AppIcons
{
    public static Icon Active { get; } = Load("Belvedere.belvedere.ico");
    public static Icon Paused { get; } = Load("Belvedere.belvedere-paused.ico");

    private static Icon Load(string resourceName)
    {
        try
        {
            using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (s is not null) return new Icon(s);
        }
        catch
        {
            // Fall through to a system default rather than failing to start.
        }
        return SystemIcons.Application;
    }
}

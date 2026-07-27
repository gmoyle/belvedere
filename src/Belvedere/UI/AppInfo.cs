using System.Reflection;

namespace Belvedere.UI;

/// <summary>Small bits of app identity shared by the tray menu and the About tab.</summary>
public static class AppInfo
{
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";
}

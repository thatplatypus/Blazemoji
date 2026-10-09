namespace Blazemoji.Desktop;

/// <summary>
/// The icon a window is given, as a file beside the program. Hermes 1.2.0 uses it on Windows
/// only. On macOS and Linux a window's icon comes from how the app is packed.
/// </summary>
public static class WindowIcon
{
    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", "logo.ico");
}

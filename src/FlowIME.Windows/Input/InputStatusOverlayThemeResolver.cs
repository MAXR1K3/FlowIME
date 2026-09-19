using Microsoft.Win32;

namespace FlowIME.Windows.Input;

internal enum OverlayColorScheme
{
    Light,
    Dark
}

internal static class InputStatusOverlayThemeResolver
{
    private const string PersonalizeRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    internal static OverlayColorScheme Resolve()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath);
            return Resolve(key?.GetValue("AppsUseLightTheme"));
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return OverlayColorScheme.Light;
        }
    }

    internal static OverlayColorScheme Resolve(object? appsUseLightTheme) =>
        appsUseLightTheme switch
        {
            int value when value == 0 => OverlayColorScheme.Dark,
            long value when value == 0 => OverlayColorScheme.Dark,
            _ => OverlayColorScheme.Light
        };
}

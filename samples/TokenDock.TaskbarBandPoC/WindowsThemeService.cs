using Microsoft.Win32;
using System;

namespace TokenDock.TaskbarBandPoC;

internal static class WindowsThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsLightTheme()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
        return key?.GetValue("AppsUseLightTheme") is not int appsUseLightTheme || appsUseLightTheme != 0;
    }
}

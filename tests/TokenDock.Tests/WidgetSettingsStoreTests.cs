using System;
using System.IO;
using System.Threading.Tasks;
using TokenDock.Services;

namespace TokenDock.Tests;

public sealed class WidgetSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsDefaultThemeMode()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "TokenDock.Tests", Guid.NewGuid().ToString("N"), "widget-settings.json");
        var store = new WidgetSettingsStore(filePath);

        var settings = await store.LoadAsync();

        Assert.Equal(ThemeMode.System, settings.ThemeMode);
    }

    [Fact]
    public async Task LoadAsync_WithLegacySettingsJson_UsesSystemThemeDefault()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TokenDock.Tests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "widget-settings.json");
        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(filePath, """
            {
              "isVisible": false,
              "isAlwaysOnTop": true,
              "opacity": 0.86,
              "mode": 0,
              "target": 0,
              "x": null,
              "y": null,
              "useCodex": true,
              "useClaude": true,
              "startWithWindows": false,
              "isTaskbarBandVisible": false,
              "taskbarBandMetricMode": 1
            }
            """);

        var store = new WidgetSettingsStore(filePath);

        var settings = await store.LoadAsync();

        Assert.Equal(ThemeMode.System, settings.ThemeMode);
    }

    [Fact]
    public async Task SaveAsync_PreservesSelectedThemeMode()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "TokenDock.Tests", Guid.NewGuid().ToString("N"), "widget-settings.json");
        var store = new WidgetSettingsStore(filePath);

        await store.SaveAsync(WidgetSettings.Default with { ThemeMode = ThemeMode.Dark });

        var settings = await store.LoadAsync();

        Assert.Equal(ThemeMode.Dark, settings.ThemeMode);
    }
}

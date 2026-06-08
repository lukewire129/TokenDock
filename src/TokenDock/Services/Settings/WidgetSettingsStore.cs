using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock.Services;

public sealed class WidgetSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public WidgetSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TokenDock",
            "widget-settings.json"))
    {
    }

    public WidgetSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<WidgetSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return WidgetSettings.Default;
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<WidgetSettings>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? WidgetSettings.Default;
    }

    public async Task SaveAsync(WidgetSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record WidgetSettings(
    bool IsVisible,
    bool IsAlwaysOnTop,
    double Opacity,
    WidgetMode Mode,
    WidgetTarget Target,
    int? X,
    int? Y,
    bool UseCodex = true,
    bool UseClaude = true)
{
    public static WidgetSettings Default { get; } = new(
        IsVisible: false,
        IsAlwaysOnTop: true,
        Opacity: 0.86,
        Mode: WidgetMode.Glass,
        Target: WidgetTarget.Auto,
        X: null,
        Y: null,
        UseCodex: true,
        UseClaude: true);
}

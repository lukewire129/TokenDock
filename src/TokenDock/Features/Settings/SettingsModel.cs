using TokenDock.Services;
using Luke.Mvux;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock;

public partial record SettingsModel
{
    private readonly WidgetSettingsStore _settingsStore;
    private readonly WindowsStartupRegistrationService _startupRegistrationService;
    private SettingsSnapshot? _lastSavedSettings;
    private CancellationTokenSource? _saveDebounceCts;
    private bool _isLoaded;

    public IState<SettingsSnapshot> Settings => State.Value(this, () => SettingsSnapshot.Default);

    public SettingsModel()
        : this(new WidgetSettingsStore(), new WindowsStartupRegistrationService())
    {
    }

    public SettingsModel(
        WidgetSettingsStore settingsStore,
        WindowsStartupRegistrationService startupRegistrationService)
    {
        _settingsStore = settingsStore;
        _startupRegistrationService = startupRegistrationService;
        _ = LoadAsync(CancellationToken.None);
    }

    public async ValueTask SetGlassMode(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { WidgetMode = WidgetMode.Glass }, cancellationToken);
    }

    public async ValueTask SetCompactMode(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { WidgetMode = WidgetMode.Compact }, cancellationToken);
    }

    public async ValueTask SetGaugeMode(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { WidgetMode = WidgetMode.Gauge }, cancellationToken);
    }

    public async ValueTask SetWidgetTargetAuto(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { WidgetTarget = WidgetTarget.Auto }, cancellationToken);
    }

    public async ValueTask SetWidgetTargetCombined(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { WidgetTarget = WidgetTarget.Combined }, cancellationToken);
    }

    public async ValueTask SetWidgetTargetCodex(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { WidgetTarget = WidgetTarget.Codex }, cancellationToken);
    }

    public async ValueTask SetWidgetTargetClaude(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { WidgetTarget = WidgetTarget.Claude }, cancellationToken);
    }

    public async ValueTask ToggleWidget(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { IsWidgetVisible = !Settings.IsWidgetVisible }, cancellationToken);
    }

    public async ValueTask ToggleAlwaysOnTop(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { IsWidgetAlwaysOnTop = !Settings.IsWidgetAlwaysOnTop }, cancellationToken);
    }

    public async ValueTask ToggleUseCodex(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { UseCodex = !Settings.UseCodex }, cancellationToken);
    }

    public async ValueTask ToggleUseClaude(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { UseClaude = !Settings.UseClaude }, cancellationToken);
    }

    public async ValueTask ToggleStartWithWindows(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { StartWithWindows = !Settings.StartWithWindows }, cancellationToken);
    }

    public async ValueTask SetSystemTheme(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { ThemeMode = ThemeMode.System }, cancellationToken);
    }

    public async ValueTask SetLightTheme(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { ThemeMode = ThemeMode.Light }, cancellationToken);
    }

    public async ValueTask SetDarkTheme(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { ThemeMode = ThemeMode.Dark }, cancellationToken);
    }

    public async ValueTask ToggleTaskbarBand(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { IsTaskbarBandVisible = !Settings.IsTaskbarBandVisible }, cancellationToken);
    }

    public async ValueTask SetTaskbarBandUsedMode(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { TaskbarBandMetricMode = TaskbarBandMetricMode.Used }, cancellationToken);
    }

    public async ValueTask SetTaskbarBandRemainingMode(CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { TaskbarBandMetricMode = TaskbarBandMetricMode.Remaining }, cancellationToken);
    }

    public async ValueTask SetWidgetOpacity(double opacity, CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { WidgetOpacity = NormalizeOpacity(opacity) }, cancellationToken);
    }

    public async ValueTask SetWidgetX(int x, CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { WidgetX = x }, cancellationToken);
    }

    public async ValueTask SetWidgetY(int y, CancellationToken cancellationToken)
    {
        await UpdateAsync(Settings => Settings with { WidgetY = y }, cancellationToken);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = ToSnapshot(await _settingsStore.LoadAsync(cancellationToken));
        await Settings.SetAsync(settings, cancellationToken);
        _startupRegistrationService.SetEnabled(settings.StartWithWindows);
        _lastSavedSettings = settings;
        _isLoaded = true;
    }

    private async ValueTask UpdateAsync(Func<SettingsSnapshot, SettingsSnapshot> update, CancellationToken cancellationToken)
    {
        var current = await Settings ?? SettingsSnapshot.Default;
        var next = Normalize(update(current));
        if (next == current)
        {
            return;
        }

        await Settings.SetAsync(next, cancellationToken);
        if (next.StartWithWindows != current.StartWithWindows)
        {
            _startupRegistrationService.SetEnabled(next.StartWithWindows);
        }

        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        _saveDebounceCts = new CancellationTokenSource();

        _ = SaveAfterDelayAsync(_saveDebounceCts.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            await SaveCurrentAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveCurrentAsync(CancellationToken cancellationToken)
    {
        if (!_isLoaded)
        {
            return;
        }

        var settings = Normalize(await Settings ?? SettingsSnapshot.Default);
        if (settings == _lastSavedSettings)
        {
            return;
        }

        await _settingsStore.SaveAsync(ToWidgetSettings(settings), cancellationToken);
        _lastSavedSettings = settings;
    }

    private static SettingsSnapshot ToSnapshot(WidgetSettings settings)
    {
        return Normalize(new SettingsSnapshot(
            settings.IsVisible,
            settings.IsAlwaysOnTop,
            Math.Clamp(settings.Opacity, 0.45, 1),
            settings.Mode,
            settings.Target,
            settings.X,
            settings.Y,
            settings.UseCodex,
            settings.UseClaude,
            settings.StartWithWindows,
            settings.IsTaskbarBandVisible,
            settings.TaskbarBandMetricMode,
            settings.ThemeMode));
    }

    private static WidgetSettings ToWidgetSettings(SettingsSnapshot settings)
    {
        return new WidgetSettings(
            settings.IsWidgetVisible,
            settings.IsWidgetAlwaysOnTop,
            Math.Clamp(settings.WidgetOpacity, 0.45, 1),
            settings.WidgetMode,
            settings.WidgetTarget,
            settings.WidgetX,
            settings.WidgetY,
            settings.UseCodex,
            settings.UseClaude,
            settings.StartWithWindows,
            settings.IsTaskbarBandVisible,
            settings.TaskbarBandMetricMode,
            settings.ThemeMode);
    }

    private static SettingsSnapshot Normalize(SettingsSnapshot settings)
    {
        var useCodex = settings.UseCodex || !settings.UseClaude;
        var useClaude = settings.UseClaude || !settings.UseCodex;

        return settings with
        {
            WidgetOpacity = NormalizeOpacity(settings.WidgetOpacity),
            UseCodex = useCodex,
            UseClaude = useClaude
        };
    }

    private static double NormalizeOpacity(double opacity)
    {
        return Math.Round(Math.Clamp(opacity, 0.45, 1), 2, MidpointRounding.AwayFromZero);
    }
}

public sealed record SettingsSnapshot(
    bool IsWidgetVisible,
    bool IsWidgetAlwaysOnTop,
    double WidgetOpacity,
    WidgetMode WidgetMode,
    WidgetTarget WidgetTarget,
    int? WidgetX,
    int? WidgetY,
    bool UseCodex,
    bool UseClaude,
    bool StartWithWindows,
    bool IsTaskbarBandVisible,
    TaskbarBandMetricMode TaskbarBandMetricMode,
    ThemeMode ThemeMode)
{
    public static SettingsSnapshot Default { get; } = new(
        IsWidgetVisible: false,
        IsWidgetAlwaysOnTop: true,
        WidgetOpacity: 0.86,
        WidgetMode: WidgetMode.Glass,
        WidgetTarget: WidgetTarget.Auto,
        WidgetX: null,
        WidgetY: null,
        UseCodex: true,
        UseClaude: true,
        StartWithWindows: false,
        IsTaskbarBandVisible: false,
        TaskbarBandMetricMode: TaskbarBandMetricMode.Remaining,
        ThemeMode: ThemeMode.System);
}

public enum WidgetMode
{
    Glass,
    Compact,
    Gauge
}

public enum WidgetTarget
{
    Auto,
    Combined,
    Codex,
    Claude
}

public enum TaskbarBandMetricMode
{
    Used,
    Remaining
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

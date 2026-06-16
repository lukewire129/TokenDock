using TokenDock.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace TokenDock;

public sealed partial class TaskbarBandWindow : Window
{
    private const int BandWidth = 220;
    private const int BandHeight = 40;
    private const int TaskbarPadding = 8;

    private readonly CodexDashboardViewModel _codexDashboard;
    private readonly ClaudeDashboardViewModel _claudeDashboard;
    private readonly SettingsViewModel _settings;
    private readonly DispatcherTimer _dockTimer;
    private nint _windowHandle;
    private RECT _lastTaskbarRect;
    private RECT _lastTrayRect;
    private bool _hasLastDockState;
    private bool? _lastLightTheme;

    public event EventHandler? MainWindowRequested;

    public TaskbarBandWindow()
        : this(new CodexDashboardViewModel(), new ClaudeDashboardViewModel(), new SettingsViewModel())
    {
    }

    public TaskbarBandWindow(
        CodexDashboardViewModel codexDashboard,
        ClaudeDashboardViewModel claudeDashboard,
        SettingsViewModel settings)
    {
        _codexDashboard = codexDashboard;
        _claudeDashboard = claudeDashboard;
        _settings = settings;

        InitializeComponent();

        _dockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _dockTimer.Tick += (_, _) => DockToTaskbar();

        Win32Properties.AddWndProcHookCallback(this, WndProcHook);
        Subscribe(_codexDashboard);
        Subscribe(_claudeDashboard);
        Subscribe(_settings);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _windowHandle = TryGetPlatformHandle()?.Handle ?? 0;
        ApplyTheme(force: true);
        UpdateContent();
        DockToTaskbar();
        _dockTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _dockTimer.Stop();
        Win32Properties.RemoveWndProcHookCallback(this, WndProcHook);
        Unsubscribe(_codexDashboard);
        Unsubscribe(_claudeDashboard);
        Unsubscribe(_settings);
        base.OnClosed(e);
    }

    private IntPtr WndProcHook(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is NativeMethods.WM_DISPLAYCHANGE)
        {
            Dispatcher.UIThread.Post(DockToTaskbar);
        }
        else if (message is NativeMethods.WM_SETTINGCHANGE)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ApplyTheme(force: true);
                DockToTaskbar();
            });
        }

        return IntPtr.Zero;
    }

    private void Dashboard_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateContent);
    }

    private void Settings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.Settings))
        {
            Dispatcher.UIThread.Post(UpdateContent);
        }
    }

    private void DockToTaskbar()
    {
        if (_windowHandle == 0)
        {
            return;
        }

        var taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle == 0 || !NativeMethods.GetWindowRect(taskbarHandle, out var taskbarRect))
        {
            return;
        }

        var trayHandle = NativeMethods.FindWindowEx(taskbarHandle, 0, "TrayNotifyWnd", null);
        var trayRect = default(RECT);
        var reservedRight = taskbarRect.Width - TaskbarPadding;
        if (trayHandle != 0 && NativeMethods.GetWindowRect(trayHandle, out trayRect))
        {
            reservedRight = Math.Max(TaskbarPadding + BandWidth, trayRect.Left - taskbarRect.Left - TaskbarPadding);
        }

        if (_hasLastDockState && _lastTaskbarRect.Equals(taskbarRect) && _lastTrayRect.Equals(trayRect))
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GWL_STYLE).ToInt64();
        var childStyle = (style & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD;
        if (style != childStyle)
        {
            NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWL_STYLE, new IntPtr(childStyle));
        }

        NativeMethods.SetParent(_windowHandle, taskbarHandle);

        var x = Math.Max(TaskbarPadding, reservedRight - BandWidth);
        var y = Math.Max(0, (taskbarRect.Height - BandHeight) / 2);

        NativeMethods.SetWindowPos(
            _windowHandle,
            0,
            x,
            y,
            BandWidth,
            BandHeight,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

        var region = NativeMethods.CreateRectRgn(0, 0, BandWidth, BandHeight);
        NativeMethods.SetWindowRgn(_windowHandle, region, redraw: true);

        _lastTaskbarRect = taskbarRect;
        _lastTrayRect = trayRect;
        _hasLastDockState = true;
    }

    private void UpdateContent()
    {
        var settings = _settings.Settings ?? SettingsSnapshot.Default;
        var metricMode = settings.TaskbarBandMetricMode;
        var codexSession = ParsePercent(_codexDashboard.FiveHourUsedPercent);
        var codexWeekly = ParsePercent(_codexDashboard.WeeklyUsedPercent);
        var claudeSession = ParsePercent(_claudeDashboard.SessionUsedPercent);
        var claudeWeekly = ParsePercent(_claudeDashboard.WeeklyUsedPercent);

        CodexUsageText.Text = FormatUsage(codexSession, codexWeekly, metricMode);
        ClaudeUsageText.Text = FormatUsage(claudeSession, claudeWeekly, metricMode);
        CodexRow.IsVisible = settings.UseCodex;
        ClaudeRow.IsVisible = settings.UseClaude;
        ToolTip.SetTip(BandRoot, CreateToolTip(settings, codexSession, codexWeekly, claudeSession, claudeWeekly));
        ApplyTheme();
        CodexUsageText.Foreground = GetUsageBrush(metricMode, codexSession, codexWeekly);
        ClaudeUsageText.Foreground = GetUsageBrush(metricMode, claudeSession, claudeWeekly);
    }

    private static string FormatUsage(double sessionUsed, double weeklyUsed, TaskbarBandMetricMode metricMode)
    {
        var session = ResolveMetric(sessionUsed, metricMode);
        var weekly = ResolveMetric(weeklyUsed, metricMode);
        return $"5h {session:0} / 7d {weekly:0}";
    }

    private static string CreateToolTip(
        SettingsSnapshot settings,
        double codexSession,
        double codexWeekly,
        double claudeSession,
        double claudeWeekly)
    {
        var metricText = settings.TaskbarBandMetricMode == TaskbarBandMetricMode.Remaining ? "남은 사용량" : "사용량";
        var codexText = FormatToolTipProvider("Codex", codexSession, codexWeekly, settings.TaskbarBandMetricMode);
        var claudeText = FormatToolTipProvider("Claude", claudeSession, claudeWeekly, settings.TaskbarBandMetricMode);

        return settings.UseCodex && settings.UseClaude
            ? $"표시 기준: {metricText}\n\n{codexText}\n\n{claudeText}"
            : settings.UseClaude
                ? $"표시 기준: {metricText}\n\n{claudeText}"
                : $"표시 기준: {metricText}\n\n{codexText}";
    }

    private static string FormatToolTipProvider(string provider, double sessionUsed, double weeklyUsed, TaskbarBandMetricMode metricMode)
    {
        var metricText = metricMode == TaskbarBandMetricMode.Remaining ? "남음" : "사용";
        var session = ResolveMetric(sessionUsed, metricMode);
        var weekly = ResolveMetric(weeklyUsed, metricMode);
        return $"{provider}\nSession {metricText} {session:0.#}%\nWeekly {metricText} {weekly:0.#}%";
    }

    private static double ResolveMetric(double used, TaskbarBandMetricMode metricMode)
    {
        return metricMode == TaskbarBandMetricMode.Remaining ? 100 - used : used;
    }

    private static double ParsePercent(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            ? Math.Clamp(percent, 0, 100)
            : 0;
    }

    private static IBrush GetUsageBrush(TaskbarBandMetricMode metricMode, double sessionUsed, double weeklyUsed)
    {
        var value = metricMode == TaskbarBandMetricMode.Remaining
            ? Math.Min(ResolveMetric(sessionUsed, metricMode), ResolveMetric(weeklyUsed, metricMode))
            : Math.Max(sessionUsed, weeklyUsed);

        if (metricMode == TaskbarBandMetricMode.Used)
        {
            return value switch
            {
                >= 90 => new SolidColorBrush(Color.Parse("#EF4444")),
                >= 70 => new SolidColorBrush(Color.Parse("#F59E0B")),
                _ => new SolidColorBrush(Color.Parse("#10A37F"))
            };
        }

        return value switch
        {
            <= 10 => new SolidColorBrush(Color.Parse("#EF4444")),
            <= 30 => new SolidColorBrush(Color.Parse("#F59E0B")),
            _ => new SolidColorBrush(Color.Parse("#10A37F"))
        };
    }

    private void ApplyTheme(bool force = false)
    {
        var settings = _settings.Settings ?? SettingsSnapshot.Default;
        var isLight = ResolveIsLightTheme(settings);
        if (!force && _lastLightTheme == isLight)
        {
            return;
        }

        _lastLightTheme = isLight;
        BandRoot.Background = Brushes.Transparent;
        BandRoot.BorderBrush = Brushes.Transparent;
        BandRoot.BorderThickness = new Thickness(0);
        var foreground = new SolidColorBrush(isLight ? Color.Parse("#111827") : Color.Parse("#F9FAFB"));
        CodexNameText.Foreground = foreground;
        ClaudeNameText.Foreground = foreground;
        if (CodexUsageText.Foreground is null)
        {
            CodexUsageText.Foreground = foreground;
        }

        if (ClaudeUsageText.Foreground is null)
        {
            ClaudeUsageText.Foreground = foreground;
        }
    }

    private static bool ResolveIsLightTheme(SettingsSnapshot settings)
    {
        return settings.ThemeMode switch
        {
            ThemeMode.Dark => false,
            ThemeMode.Light => true,
            _ => WindowsThemeService.IsLightTheme()
        };
    }

    private void BandRoot_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            MainWindowRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Subscribe(INotifyPropertyChanged source)
    {
        if (ReferenceEquals(source, _settings))
        {
            source.PropertyChanged += Settings_OnPropertyChanged;
            return;
        }

        source.PropertyChanged += Dashboard_OnPropertyChanged;
    }

    private void Unsubscribe(INotifyPropertyChanged source)
    {
        if (ReferenceEquals(source, _settings))
        {
            source.PropertyChanged -= Settings_OnPropertyChanged;
            return;
        }

        source.PropertyChanged -= Dashboard_OnPropertyChanged;
    }
}

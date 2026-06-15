using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace TokenDock.TaskbarBandPoC;

public sealed partial class TaskbarBandWindow : Window
{
    private const int BandWidth = 240;
    private const int BandHeight = 34;
    private const int TaskbarPadding = 8;

    private readonly DispatcherTimer _dockTimer;
    private nint _windowHandle;
    private nint _taskbarHandle;
    private RECT _lastTaskbarRect;
    private RECT _lastTrayRect;
    private bool _hasLastDockState;
    private bool? _lastLightTheme;

    public TaskbarBandWindow()
    {
        InitializeComponent();

        _dockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _dockTimer.Tick += (_, _) => DockToTaskbar();

        Win32Properties.AddWndProcHookCallback(this, WndProcHook);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _windowHandle = TryGetPlatformHandle()?.Handle ?? 0;
        ApplyTheme(force: true);
        DockToTaskbar();
        _dockTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _dockTimer.Stop();
        Win32Properties.RemoveWndProcHookCallback(this, WndProcHook);
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

    private void DockToTaskbar()
    {
        if (_windowHandle == 0)
        {
            return;
        }

        _taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (_taskbarHandle == 0 || !NativeMethods.GetWindowRect(_taskbarHandle, out var taskbarRect))
        {
            return;
        }

        var trayHandle = NativeMethods.FindWindowEx(_taskbarHandle, 0, "TrayNotifyWnd", null);
        var trayRect = default(RECT);
        var reservedRight = taskbarRect.Width - TaskbarPadding;
        if (trayHandle != 0 && NativeMethods.GetWindowRect(trayHandle, out trayRect))
        {
            reservedRight = Math.Max(TaskbarPadding + BandWidth, trayRect.Left - taskbarRect.Left - TaskbarPadding);
        }

        if (_hasLastDockState
            && _lastTaskbarRect.Equals(taskbarRect)
            && _lastTrayRect.Equals(trayRect))
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GWL_STYLE).ToInt64();
        var childStyle = (style & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD;
        if (style != childStyle)
        {
            NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWL_STYLE, new IntPtr(childStyle));
        }

        NativeMethods.SetParent(_windowHandle, _taskbarHandle);

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

    private void ApplyTheme(bool force = false)
    {
        var isLight = WindowsThemeService.IsLightTheme();
        if (!force && _lastLightTheme == isLight)
        {
            return;
        }

        _lastLightTheme = isLight;
        BandRoot.Background = Brushes.Transparent;
        BandRoot.BorderBrush = Brushes.Transparent;
        BandRoot.BorderThickness = new Thickness(0);
        TitleText.Foreground = new SolidColorBrush(isLight ? Color.Parse("#111827") : Color.Parse("#F9FAFB"));
        UsageText.Foreground = new SolidColorBrush(isLight ? Color.Parse("#4B5563") : Color.Parse("#D1D5DB"));
    }

}

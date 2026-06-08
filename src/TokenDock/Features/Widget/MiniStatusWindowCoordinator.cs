using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.ComponentModel;

namespace TokenDock;

public sealed class MiniStatusWindowCoordinator : IDisposable
{
    private readonly MainWindow _mainWindow;
    private MiniStatusWindow? _window;
    private Action<string>? _toggleHeaderChanged;
    private bool? _lastVisible;

    public MiniStatusWindowCoordinator(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _mainWindow.SettingsViewModel.PropertyChanged += Settings_OnPropertyChanged;
        Sync();
    }

    public event Action<string>? ToggleHeaderChanged
    {
        add
        {
            _toggleHeaderChanged += value;
            value?.Invoke(GetToggleHeader(CurrentSettings.IsWidgetVisible));
        }
        remove => _toggleHeaderChanged -= value;
    }

    public void ToggleWidget()
    {
        var command = _mainWindow.SettingsViewModel.ToggleWidget;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    public void Close()
    {
        _window?.Close();
        _window = null;
    }

    public void Dispose()
    {
        _mainWindow.SettingsViewModel.PropertyChanged -= Settings_OnPropertyChanged;
        Close();
    }

    private void Settings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(SettingsViewModel.Settings))
        {
            return;
        }

        var isVisible = CurrentSettings.IsWidgetVisible;
        if (isVisible != _lastVisible)
        {
            Dispatcher.UIThread.Post(Sync);
        }
    }

    private void Sync()
    {
        var isVisible = CurrentSettings.IsWidgetVisible;
        _lastVisible = isVisible;
        _toggleHeaderChanged?.Invoke(GetToggleHeader(isVisible));

        if (!isVisible)
        {
            _window?.Hide();
            return;
        }

        if (_window is null)
        {
            _window = new MiniStatusWindow(
                _mainWindow.DashboardViewModel,
                _mainWindow.ClaudeDashboardViewModel,
                _mainWindow.SettingsViewModel);
            _window.MainWindowRequested += MiniStatusWindow_OnMainWindowRequested;
        }

        _window.Show();
    }

    private void MiniStatusWindow_OnMainWindowRequested(object? sender, EventArgs e)
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private SettingsSnapshot CurrentSettings => _mainWindow.SettingsViewModel.Settings ?? SettingsSnapshot.Default;

    private static string GetToggleHeader(bool isVisible)
    {
        return isVisible ? "미니 위젯 숨기기" : "미니 위젯 표시";
    }
}

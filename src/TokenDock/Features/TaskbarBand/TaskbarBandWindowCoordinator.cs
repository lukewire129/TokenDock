using System;
using System.ComponentModel;

namespace TokenDock;

public sealed class TaskbarBandWindowCoordinator : IDisposable
{
    private readonly MainWindow _mainWindow;
    private TaskbarBandWindow? _window;

    public TaskbarBandWindowCoordinator(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _mainWindow.SettingsViewModel.PropertyChanged += Settings_OnPropertyChanged;
        ApplyVisibility();
    }

    public void Dispose()
    {
        _mainWindow.SettingsViewModel.PropertyChanged -= Settings_OnPropertyChanged;
        CloseWindow();
    }

    private void Settings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.Settings))
        {
            ApplyVisibility();
        }
    }

    private void ApplyVisibility()
    {
        var settings = _mainWindow.SettingsViewModel.Settings ?? SettingsSnapshot.Default;
        if (settings.IsTaskbarBandVisible)
        {
            EnsureWindow();
            return;
        }

        CloseWindow();
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new TaskbarBandWindow(
            _mainWindow.DashboardViewModel,
            _mainWindow.ClaudeDashboardViewModel,
            _mainWindow.SettingsViewModel);
        _window.MainWindowRequested += TaskbarBandWindow_OnMainWindowRequested;
        _window.Show();
    }

    private void CloseWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.MainWindowRequested -= TaskbarBandWindow_OnMainWindowRequested;
        _window.Close();
        _window = null;
    }

    private void TaskbarBandWindow_OnMainWindowRequested(object? sender, EventArgs e)
    {
        _mainWindow.Show();
        _mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
        _mainWindow.Activate();
    }
}

using Avalonia.Controls;
using System.ComponentModel;

namespace TokenDock;

public sealed class MainWindowCoordinator
{
    private readonly MainViewModel _shell;
    private readonly CodexDashboardViewModel _codexDashboard;
    private readonly ClaudeDashboardViewModel _claudeDashboard;
    private readonly SettingsViewModel _settings;
    private readonly ContentControl _pageHost;
    private readonly Control _codexDashboardButton;
    private readonly Control _claudeDashboardButton;
    private SettingsSnapshot _lastAppliedSettings = SettingsSnapshot.Default;

    public MainWindowCoordinator(
        MainViewModel shell,
        CodexDashboardViewModel codexDashboard,
        ClaudeDashboardViewModel claudeDashboard,
        SettingsViewModel settings,
        ContentControl pageHost,
        Control codexDashboardButton,
        Control claudeDashboardButton)
    {
        _shell = shell;
        _codexDashboard = codexDashboard;
        _claudeDashboard = claudeDashboard;
        _settings = settings;
        _pageHost = pageHost;
        _codexDashboardButton = codexDashboardButton;
        _claudeDashboardButton = claudeDashboardButton;

        _settings.PropertyChanged += Settings_OnPropertyChanged;
        _shell.PropertyChanged += Shell_OnPropertyChanged;

        ApplySettingsState();
        SetPageContent();
    }

    public void RefreshCurrentDashboard()
    {
        var shell = _shell.Shell ?? MainShellSnapshot.Default;
        var settings = _settings.Settings ?? SettingsSnapshot.Default;

        if (shell.CurrentDashboard == DashboardProvider.Claude)
        {
            ExecuteRefresh(_claudeDashboard.Refresh, settings.UseClaude);
            return;
        }

        ExecuteRefresh(_codexDashboard.Refresh, settings.UseCodex);
    }

    public void RefreshConfiguredDashboard()
    {
        var settings = _settings.Settings ?? SettingsSnapshot.Default;
        var target = MiniStatusWidgetModel.ResolveTarget(_settings);

        if (target is EffectiveWidgetTarget.Codex or EffectiveWidgetTarget.Combined)
        {
            ExecuteRefresh(_codexDashboard.Refresh, settings.UseCodex);
        }

        if (target is EffectiveWidgetTarget.Claude or EffectiveWidgetTarget.Combined)
        {
            ExecuteRefresh(_claudeDashboard.Refresh, settings.UseClaude);
        }
    }

    private void Settings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(SettingsViewModel.Settings))
        {
            return;
        }

        var settings = _settings.Settings ?? SettingsSnapshot.Default;
        var dashboardVisibilityChanged = settings.UseCodex != _lastAppliedSettings.UseCodex
            || settings.UseClaude != _lastAppliedSettings.UseClaude;

        if (dashboardVisibilityChanged)
        {
            ApplySettingsState();
            SetPageContent();
        }
    }

    private void Shell_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Shell))
        {
            SetPageContent();
        }
    }

    private void SetPageContent()
    {
        var shell = _shell.Shell ?? MainShellSnapshot.Default;
        _pageHost.Content = shell.CurrentPage switch
        {
            MainPage.Settings => _settings,
            _ when shell.CurrentDashboard == DashboardProvider.Claude => _claudeDashboard,
            _ => _codexDashboard
        };
    }

    private void ApplySettingsState()
    {
        var settings = _settings.Settings ?? SettingsSnapshot.Default;
        DashboardRuntimeSettings.UseCodex = settings.UseCodex;
        DashboardRuntimeSettings.UseClaude = settings.UseClaude;
        _lastAppliedSettings = settings;
        _codexDashboardButton.IsVisible = settings.UseCodex;
        _claudeDashboardButton.IsVisible = settings.UseClaude;

        var shell = _shell.Shell ?? MainShellSnapshot.Default;
        if (shell.CurrentDashboard == DashboardProvider.Codex && !settings.UseCodex && settings.UseClaude)
        {
            _shell.ShowClaudeDashboard.Execute(null);
        }
        else if (shell.CurrentDashboard == DashboardProvider.Claude && !settings.UseClaude && settings.UseCodex)
        {
            _shell.ShowCodexDashboard.Execute(null);
        }
    }

    private static void ExecuteRefresh(global::Luke.Mvux.IAsyncCommand command, bool isEnabled)
    {
        if (isEnabled && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}

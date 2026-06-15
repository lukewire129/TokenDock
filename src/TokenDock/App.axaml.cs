using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using System;

namespace TokenDock
{
    public partial class App : Application
    {
        private bool _isExitRequested;
        private MainWindow? _mainWindow;
        private MiniStatusWindowCoordinator? _miniStatusWindowCoordinator;
        private TaskbarBandWindowCoordinator? _taskbarBandWindowCoordinator;
        private TrayIcon? _trayIcon;
        private NativeMenuItem? _toggleWidgetItem;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _mainWindow = new MainWindow();
                _mainWindow.CloseRequested += MainWindow_OnCloseRequested;
                desktop.MainWindow = _mainWindow;
                _trayIcon = CreateTrayIcon();
                _miniStatusWindowCoordinator = new MiniStatusWindowCoordinator(_mainWindow);
                _miniStatusWindowCoordinator.ToggleHeaderChanged += SetToggleWidgetHeader;
                _taskbarBandWindowCoordinator = new TaskbarBandWindowCoordinator(_mainWindow);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void TrayIcon_OnClicked(object? sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void ShowWindow_OnClick(object? sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void Refresh_OnClick(object? sender, EventArgs e)
        {
            _mainWindow?.RefreshUsage();
        }

        private void ToggleWidget_OnClick(object? sender, EventArgs e)
        {
            _miniStatusWindowCoordinator?.ToggleWidget();
        }

        private void Exit_OnClick(object? sender, EventArgs e)
        {
            _isExitRequested = true;
            _miniStatusWindowCoordinator?.Dispose();
            _miniStatusWindowCoordinator = null;
            _taskbarBandWindowCoordinator?.Dispose();
            _taskbarBandWindowCoordinator = null;
            _trayIcon?.Dispose();
            _trayIcon = null;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        private void MainWindow_OnCloseRequested(object? sender, WindowCloseRequestedEventArgs e)
        {
            if (_isExitRequested)
            {
                return;
            }

            e.Cancel = true;
            _mainWindow?.Hide();
        }

        private void ShowMainWindow()
        {
            if (_mainWindow is null)
            {
                return;
            }

            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        private void SetToggleWidgetHeader(string header)
        {
            if (_toggleWidgetItem is not null)
            {
                _toggleWidgetItem.Header = header;
            }
        }

        private TrayIcon CreateTrayIcon()
        {
            var showItem = new NativeMenuItem("열기");
            showItem.Click += ShowWindow_OnClick;

            var refreshItem = new NativeMenuItem("새로고침");
            refreshItem.Click += Refresh_OnClick;

            _toggleWidgetItem = new NativeMenuItem("미니 위젯 표시");
            _toggleWidgetItem.Click += ToggleWidget_OnClick;

            var exitItem = new NativeMenuItem("종료");
            exitItem.Click += Exit_OnClick;

            var menu = new NativeMenu();
            menu.Add(showItem);
            menu.Add(refreshItem);
            menu.Add(_toggleWidgetItem);
            menu.Add(exitItem);

            var iconStream = AssetLoader.Open(new Uri("avares://TokenDock/Assets/AppIcon.ico"));
            var trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(iconStream),
                IsVisible = true,
                Menu = menu,
                ToolTipText = "TokenDock"
            };

            trayIcon.Clicked += TrayIcon_OnClicked;
            return trayIcon;
        }
    }
}

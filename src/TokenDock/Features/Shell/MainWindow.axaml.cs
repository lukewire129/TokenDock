using Avalonia.Controls;
using System;

namespace TokenDock
{
    public partial class MainWindow : Window
    {
        public event EventHandler<WindowCloseRequestedEventArgs>? CloseRequested;
        public CodexDashboardViewModel DashboardViewModel { get; } = new();
        public ClaudeDashboardViewModel ClaudeDashboardViewModel { get; } = new();
        public SettingsViewModel SettingsViewModel { get; } = new();
        private MainWindowCoordinator Coordinator { get; }

        public MainWindow()
        {
            InitializeComponent();
            var viewModel = new MainViewModel();
            DataContext = viewModel;
            ToastHost.DataContext = DashboardViewModel;
            Coordinator = new MainWindowCoordinator(
                viewModel,
                DashboardViewModel,
                ClaudeDashboardViewModel,
                SettingsViewModel,
                PageHost,
                CodexDashboardButton,
                ClaudeDashboardButton);
        }

        public void RefreshUsage()
        {
            Coordinator.RefreshConfiguredDashboard();
        }

        private void Refresh_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Coordinator.RefreshCurrentDashboard();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            var args = new WindowCloseRequestedEventArgs();
            CloseRequested?.Invoke(this, args);
            e.Cancel = args.Cancel;
        }
    }

    public sealed class WindowCloseRequestedEventArgs : EventArgs
    {
        public bool Cancel { get; set; }
    }
}

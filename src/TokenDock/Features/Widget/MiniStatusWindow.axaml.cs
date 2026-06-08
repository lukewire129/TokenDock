using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace TokenDock;

public partial class MiniStatusWindow : Window
{
    private readonly SettingsViewModel _settings;
    private bool _isPositionLoaded;
    private PixelPoint? _pendingPosition;

    public event EventHandler? MainWindowRequested;

    public MiniStatusWindow()
        : this(new CodexDashboardViewModel(), new ClaudeDashboardViewModel(), new SettingsViewModel())
    {
    }

    public MiniStatusWindow(CodexDashboardViewModel codexDashboard, ClaudeDashboardViewModel claudeDashboard, SettingsViewModel settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = new MiniStatusWidgetViewModel(codexDashboard, claudeDashboard, settings);
        PositionChanged += MiniStatusWindow_OnPositionChanged;
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);

        var settings = CurrentSettings;
        if (settings.WidgetX is { } x && settings.WidgetY is { } y)
        {
            Position = new PixelPoint(x, y);
        }

        _isPositionLoaded = true;
    }

    private void MiniStatusWindow_OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_isPositionLoaded)
        {
            _pendingPosition = Position;
        }
    }

    private void Widget_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                MainWindowRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            BeginMoveDrag(e);
            SavePendingPosition();
        }
    }

    private void SavePendingPosition()
    {
        if (_pendingPosition is not { } position)
        {
            return;
        }

        _pendingPosition = null;
        if (_settings.SetWidgetX.CanExecute(position.X))
        {
            _settings.SetWidgetX.Execute(position.X);
        }

        if (_settings.SetWidgetY.CanExecute(position.Y))
        {
            _settings.SetWidgetY.Execute(position.Y);
        }
    }

    private SettingsSnapshot CurrentSettings => _settings.Settings ?? SettingsSnapshot.Default;
}

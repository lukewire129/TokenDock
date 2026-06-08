using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System;
using System.Windows.Input;

namespace TokenDock;

public static class SliderValueCommand
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<RangeBase, ICommand?>("Command", typeof(SliderValueCommand));

    private static readonly AttachedProperty<double?> LastSentValueProperty =
        AvaloniaProperty.RegisterAttached<RangeBase, double?>("LastSentValue", typeof(SliderValueCommand));

    private static readonly AttachedProperty<bool> IsInteractingProperty =
        AvaloniaProperty.RegisterAttached<RangeBase, bool>("IsInteracting", typeof(SliderValueCommand));

    static SliderValueCommand()
    {
        CommandProperty.Changed.AddClassHandler<RangeBase>(OnCommandChanged);
    }

    public static ICommand? GetCommand(RangeBase control)
    {
        return control.GetValue(CommandProperty);
    }

    public static void SetCommand(RangeBase control, ICommand? value)
    {
        control.SetValue(CommandProperty, value);
    }

    private static void OnCommandChanged(RangeBase control, AvaloniaPropertyChangedEventArgs e)
    {
        control.ValueChanged -= Control_OnValueChanged;
        control.PointerPressed -= Control_OnPointerPressed;
        control.PointerReleased -= Control_OnPointerReleased;
        control.KeyDown -= Control_OnKeyDown;
        control.KeyUp -= Control_OnKeyUp;

        if (e.NewValue is not null)
        {
            control.ValueChanged += Control_OnValueChanged;
            control.PointerPressed += Control_OnPointerPressed;
            control.PointerReleased += Control_OnPointerReleased;
            control.KeyDown += Control_OnKeyDown;
            control.KeyUp += Control_OnKeyUp;
        }
    }

    private static void Control_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not RangeBase control)
        {
            return;
        }

        var command = GetCommand(control);
        if (command is null)
        {
            return;
        }

        if (!control.GetValue(IsInteractingProperty))
        {
            return;
        }

        var value = Math.Round(Math.Clamp(e.NewValue, control.Minimum, control.Maximum), 2, MidpointRounding.AwayFromZero);
        if (control.GetValue(LastSentValueProperty) == value)
        {
            return;
        }

        control.SetValue(LastSentValueProperty, value);
        if (command.CanExecute(value))
        {
            command.Execute(value);
        }
    }

    private static void Control_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is RangeBase control)
        {
            control.SetValue(IsInteractingProperty, true);
        }
    }

    private static void Control_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is RangeBase control)
        {
            control.SetValue(IsInteractingProperty, false);
        }
    }

    private static void Control_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is RangeBase control)
        {
            control.SetValue(IsInteractingProperty, true);
        }
    }

    private static void Control_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is RangeBase control)
        {
            control.SetValue(IsInteractingProperty, false);
        }
    }
}

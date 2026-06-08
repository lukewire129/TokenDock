using TokenDock.Services;
using Luke.Mvux;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock;

public sealed class ToastModel
{
    private readonly TimeSpan _displayDuration;
    private int _version;

    public static ToastModel Shared { get; } = new();

    public ToastModel()
        : this(TimeSpan.FromSeconds(4))
    {
    }

    public ToastModel(TimeSpan displayDuration)
    {
        _displayDuration = displayDuration;
    }

    public IState<ToastState> Current => State.Value(this, ToastState.Hidden);

    public void Show(string message, ToastSeverity severity)
    {
        var version = Interlocked.Increment(ref _version);
        _ = ShowAsync(message, severity, version);
    }

    private async Task ShowAsync(string message, ToastSeverity severity, int version)
    {
        await Current.SetAsync(ToastState.Visible(message, severity), CancellationToken.None);
        await Task.Delay(_displayDuration, CancellationToken.None);

        if (version == Volatile.Read(ref _version))
        {
            await Current.SetAsync(ToastState.Hidden(), CancellationToken.None);
        }
    }
}

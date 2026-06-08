using TokenDock.Services;

namespace TokenDock;

public sealed record ToastState(
    bool IsVisible,
    string Message,
    string Background,
    string BorderBrush,
    string Foreground)
{
    public static ToastState Hidden()
    {
        return new ToastState(false, string.Empty, "#FFFFFF", "#E5E7EB", "#1F2937");
    }

    public static ToastState Visible(string message, ToastSeverity severity)
    {
        return severity switch
        {
            ToastSeverity.Danger => new ToastState(true, message, "#FEF2F2", "#FCA5A5", "#991B1B"),
            ToastSeverity.Warning => new ToastState(true, message, "#FFFBEB", "#FCD34D", "#92400E"),
            _ => new ToastState(true, message, "#ECFDF5", "#86EFAC", "#065F46")
        };
    }
}

using TokenDock.Services;

namespace TokenDock.Tests;

public sealed class ToastStateTests
{
    [Fact]
    public void Hidden_ReturnsInvisibleState()
    {
        var state = ToastState.Hidden();

        Assert.False(state.IsVisible);
        Assert.Equal(string.Empty, state.Message);
    }

    [Fact]
    public void Visible_ReturnsDangerColorsForDangerSeverity()
    {
        var state = ToastState.Visible("error", ToastSeverity.Danger);

        Assert.True(state.IsVisible);
        Assert.Equal("error", state.Message);
        Assert.Equal("#FEF2F2", state.Background);
        Assert.Equal("#FCA5A5", state.BorderBrush);
        Assert.Equal("#991B1B", state.Foreground);
    }
}

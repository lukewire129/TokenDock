using Luke.Mvux;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock;

public partial record MainModel
{
    public IState<MainShellSnapshot> Shell => State.Value(this, () => MainShellSnapshot.Default);

    public async ValueTask ShowDashboard(CancellationToken cancellationToken)
    {
        await UpdateShellAsync(shell => shell with { CurrentPage = MainPage.Dashboard }, cancellationToken);
    }

    public async ValueTask ShowSettings(CancellationToken cancellationToken)
    {
        await UpdateShellAsync(shell => shell with { CurrentPage = MainPage.Settings }, cancellationToken);
    }

    public async ValueTask ShowCodexDashboard(CancellationToken cancellationToken)
    {
        await UpdateShellAsync(shell => shell with { CurrentDashboard = DashboardProvider.Codex }, cancellationToken);
    }

    public async ValueTask ShowClaudeDashboard(CancellationToken cancellationToken)
    {
        await UpdateShellAsync(shell => shell with { CurrentDashboard = DashboardProvider.Claude }, cancellationToken);
    }

    private async ValueTask UpdateShellAsync(
        System.Func<MainShellSnapshot, MainShellSnapshot> update,
        CancellationToken cancellationToken)
    {
        var current = await Shell ?? MainShellSnapshot.Default;
        await Shell.SetAsync(update(current), cancellationToken);
    }
}

public sealed record MainShellSnapshot(MainPage CurrentPage, DashboardProvider CurrentDashboard)
{
    public static MainShellSnapshot Default { get; } = new(MainPage.Dashboard, DashboardProvider.Codex);
}

public static class DashboardRuntimeSettings
{
    public static volatile bool UseCodex = true;

    public static volatile bool UseClaude = true;
}

public enum MainPage
{
    Dashboard,
    Settings
}

public enum DashboardProvider
{
    Codex,
    Claude
}

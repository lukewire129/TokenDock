using TokenDock.Services;
using Luke.Mvux;
using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock;

public partial record ClaudeDashboardModel
{
    private readonly ClaudeUsageService _claudeUsageService;
    private readonly ClaudeUsageAlertTracker _usageAlertTracker;
    private readonly ToastModel _toastModel;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);
    private readonly object _refreshDelayLock = new();
    private CancellationTokenSource _refreshDelayCts = new();

    public IState<string> Plan => State.Value(this, () => "Claude");

    public IState<string> SessionLimit => State.Value(this, () => "--% 남음");

    public IState<string> SessionLimitTimeText => State.Value(this, () => "Claude 연결 필요");

    public IState<string> SessionUsedPercent => State.Value(this, () => "0");

    public IState<string> WeeklyLimit => State.Value(this, () => "--% 남음");

    public IState<string> WeeklyLimitTimeText => State.Value(this, () => "Claude 연결 필요");

    public IState<string> WeeklyUsedPercent => State.Value(this, () => "0");

    public IState<string> Status => State.Value(this, () => "Claude CLI OAuth usage 연결 준비 중");

    public IState<string> NextRefreshText => State.Value(this, () => "Claude 사용량 조회 미연결");

    public IState<string> RateLimitStatus => State.Value(this, () => "계정 연결 후 확인");

    public IState<string> CreditStatus => State.Value(this, () => "정보 없음");

    public IState<ClaudeConnectionState> Connection => State.Value(this, () => new ClaudeConnectionState(false));

    public IState<ToastState> Toast => _toastModel.Current;

    public ClaudeDashboardModel()
        : this(new ClaudeUsageService(new HttpClient()), new ClaudeUsageAlertTracker(), ToastModel.Shared)
    {
    }

    public ClaudeDashboardModel(ClaudeUsageService claudeUsageService)
        : this(claudeUsageService, new ClaudeUsageAlertTracker(), ToastModel.Shared)
    {
    }

    public ClaudeDashboardModel(
        ClaudeUsageService claudeUsageService,
        ClaudeUsageAlertTracker usageAlertTracker,
        ToastModel toastModel)
    {
        _claudeUsageService = claudeUsageService;
        _usageAlertTracker = usageAlertTracker;
        _toastModel = toastModel;
        _ = RunRefreshLoopAsync(CancellationToken.None);
    }

    public async ValueTask Refresh(CancellationToken cancellationToken)
    {
        if (!DashboardRuntimeSettings.UseClaude)
        {
            await Status.SetAsync("설정에서 Claude가 꺼져 있습니다", cancellationToken);
            await NextRefreshText.SetAsync("Claude 사용 안 함", cancellationToken);
            return;
        }

        await RefreshAsync(forceRefresh: true, cancellationToken);
        RestartRefreshDelay();
    }

    public async ValueTask Diagnose(CancellationToken cancellationToken)
    {
        if (!DashboardRuntimeSettings.UseClaude)
        {
            await Status.SetAsync("설정에서 Claude가 꺼져 있습니다", cancellationToken);
            await NextRefreshText.SetAsync("Claude 사용 안 함", cancellationToken);
            return;
        }

        await Status.SetAsync("Claude 인증 상태 확인 중...", cancellationToken);
        var diagnose = await _claudeUsageService.DiagnoseAsync(cancellationToken);
        await Status.SetAsync(diagnose, cancellationToken);
        await NextRefreshText.SetAsync("Claude 인증 진단 완료", cancellationToken);
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (DashboardRuntimeSettings.UseClaude)
                {
                    await RefreshAsync(forceRefresh: false, cancellationToken);
                }
                else
                {
                    await NextRefreshText.SetAsync("Claude 사용 안 함", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                await Status.SetAsync(ex.Message, cancellationToken);
                await NextRefreshText.SetAsync("Claude 자동 갱신 실패", cancellationToken);
            }

            await WaitForNextRefreshAsync(cancellationToken);
        }
    }

    private async Task WaitForNextRefreshAsync(CancellationToken cancellationToken)
    {
        var totalSeconds = (int)_interval.TotalSeconds;
        var remaining = totalSeconds;

        while (remaining > 0)
        {
            if (DashboardRuntimeSettings.UseClaude)
            {
                await NextRefreshText.SetAsync($"다음 리프레쉬 {FormatRefreshDelay(remaining)} 후", cancellationToken);
            }

            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                GetRefreshDelayToken());

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), delayCts.Token);
                remaining--;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                remaining = totalSeconds;
            }
        }
    }

    private CancellationToken GetRefreshDelayToken()
    {
        lock (_refreshDelayLock)
        {
            return _refreshDelayCts.Token;
        }
    }

    private void RestartRefreshDelay()
    {
        CancellationTokenSource previous;
        lock (_refreshDelayLock)
        {
            previous = _refreshDelayCts;
            _refreshDelayCts = new CancellationTokenSource();
        }

        previous.Cancel();
    }

    private async ValueTask RefreshAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await Status.SetAsync("Claude usage 조회 중...", cancellationToken);
        await NextRefreshText.SetAsync("리프레쉬 중...", cancellationToken);

        try
        {
            var usage = await _claudeUsageService.GetUsageSnapshotAsync(forceRefresh, cancellationToken);
            await Connection.SetAsync(new ClaudeConnectionState(true), cancellationToken);
            await Plan.SetAsync(string.IsNullOrWhiteSpace(usage.Plan) ? "Claude" : usage.Plan.ToUpper(), cancellationToken);
            await ApplyWindowAsync(usage.Session, SessionLimit, SessionLimitTimeText, SessionUsedPercent, cancellationToken);
            await ApplyWindowAsync(usage.Weekly, WeeklyLimit, WeeklyLimitTimeText, WeeklyUsedPercent, cancellationToken);
            await RateLimitStatus.SetAsync(FormatRateLimitStatus(usage), cancellationToken);
            await CreditStatus.SetAsync(FormatCreditStatus(usage), cancellationToken);
            await Status.SetAsync($"마지막 확인 {DateTime.Now:HH:mm:ss}", cancellationToken);
            await NextRefreshText.SetAsync($"다음 Claude 리프레쉬 {FormatRefreshDelay((int)_interval.TotalSeconds)} 후", cancellationToken);

            foreach (var alert in _usageAlertTracker.Evaluate(usage))
            {
                _toastModel.Show(alert.Message, alert.Severity);
            }
        }
        catch (Exception ex)
        {
            await Connection.SetAsync(new ClaudeConnectionState(false), cancellationToken);
            var diagnose = await _claudeUsageService.DiagnoseAsync(cancellationToken);
            await Status.SetAsync($"{ex.Message}\n{diagnose}", cancellationToken);
            await NextRefreshText.SetAsync("Claude 사용량 조회 실패", cancellationToken);
            _toastModel.Show(ex.Message, GetErrorSeverity(ex));
        }
    }

    private static async ValueTask ApplyWindowAsync(
        ClaudeRateWindow? window,
        IState<string> remainingText,
        IState<string> resetTimeText,
        IState<string> usedPercent,
        CancellationToken cancellationToken)
    {
        if (window is null)
        {
            await remainingText.SetAsync("--% 남음", cancellationToken);
            await resetTimeText.SetAsync("정보 없음", cancellationToken);
            await usedPercent.SetAsync("0", cancellationToken);
            return;
        }

        var used = Math.Clamp(window.Utilization, 0, 100);
        var remaining = 100 - used;
        await remainingText.SetAsync($"{remaining:0.#}% 남음", cancellationToken);
        await resetTimeText.SetAsync(string.IsNullOrWhiteSpace(window.ResetDescription)
            ? "리셋 시간 없음"
            : $"{window.ResetDescription} 후 리셋", cancellationToken);
        await usedPercent.SetAsync(used.ToString("0.#", CultureInfo.InvariantCulture), cancellationToken);
    }

    private static string FormatRateLimitStatus(ClaudeUsageSnapshot usage)
    {
        return usage.Session is null && usage.Weekly is null
            ? "정보 없음"
            : "제한 없음";
    }

    private static string FormatCreditStatus(ClaudeUsageSnapshot usage)
    {
        return usage.ModelWindows.Count == 0
            ? "모델별 정보 없음"
            : $"모델별 {usage.ModelWindows.Count}개";
    }

    private static string FormatRefreshDelay(int totalSeconds)
    {
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes}분 {seconds:00}초";
    }

    private static ToastSeverity GetErrorSeverity(Exception exception)
    {
        return exception is ClaudeTokenExpiredException
            or HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden }
            or System.Text.Json.JsonException
            ? ToastSeverity.Danger
            : ToastSeverity.Warning;
    }
}

public sealed record ClaudeConnectionState(bool IsConnected)
{
    public bool IsDisconnected => !IsConnected;
}

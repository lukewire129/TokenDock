using TokenDock.Services;
using Luke.Mvux;
using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock;

public partial record CodexDashboardModel
{
    private readonly OpenAiUsageService _openAiUsageService;
    private readonly OpenAiBrowserLoginService _browserLoginService;
    private readonly CodexAuthStore _authStore;
    private readonly CodexUsageAlertTracker _usageAlertTracker;
    private readonly ToastModel _toastModel;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);
    private readonly object _refreshDelayLock = new();
    private CancellationTokenSource _refreshDelayCts = new();

    public IState<string> Plan => State.Value(this, () => "Token required");
    public IState<string> FiveHourLimit => State.Value(this, () => "--% 남음");
    public IState<string> FiveHourLimitTimeText => State.Value(this, () => "토큰 설정 필요");
    public IState<string> FiveHourUsedPercent => State.Value(this, () => "0");
    public IState<string> WeeklyLimit => State.Value(this, () => "--% 남음");
    public IState<string> WeeklyLimitTimeText => State.Value(this, () => "토큰 설정 필요");
    public IState<string> WeeklyUsedPercent => State.Value(this, () => "0");
    public IState<string> NextRefreshText => State.Value(this, () => "다음 리프레쉬 --초 후");
    public IState<string> Status => State.Value(this, () => "로그인 토큰을 저장하세요");
    public IState<string> RateLimitStatus => State.Value(this, () => "계정 연결 후 확인");
    public IState<string> CreditStatus => State.Value(this, () => "계정 연결 후 확인");
    public IState<AuthConnectionState> AuthConnection => State.Value(this, () => new AuthConnectionState(false));
    public IState<ToastState> Toast => _toastModel.Current;

    public CodexDashboardModel()
        : this(
            new OpenAiUsageService(new HttpClient()),
            new OpenAiBrowserLoginService(new HttpClient()),
            new CodexAuthStore(),
            new CodexUsageAlertTracker(),
            ToastModel.Shared)
    {
    }

    public CodexDashboardModel(
        OpenAiUsageService openAiUsageService,
        OpenAiBrowserLoginService browserLoginService,
        CodexAuthStore authStore,
        CodexUsageAlertTracker usageAlertTracker,
        ToastModel toastModel)
    {
        _openAiUsageService = openAiUsageService;
        _browserLoginService = browserLoginService;
        _authStore = authStore;
        _usageAlertTracker = usageAlertTracker;
        _toastModel = toastModel;
        _ = RunRefreshLoopAsync(CancellationToken.None);
    }

    public async ValueTask ConnectAccount(CancellationToken cancellationToken)
    {
        await Status.SetAsync("브라우저에서 OpenAI 계정을 연결하세요", cancellationToken);
        var tokens = await _browserLoginService.LoginAsync(cancellationToken);
        await _authStore.SaveAsync(tokens, cancellationToken);
        await SetAuthConnectionAsync(true, cancellationToken);
        await Status.SetAsync("계정 연결 완료. 토큰을 암호화해서 저장했습니다", cancellationToken);
        _toastModel.Show("계정 연결이 완료되었습니다.", ToastSeverity.Info);
        await Refresh(cancellationToken);
    }

    public async ValueTask ClearAuth(CancellationToken cancellationToken)
    {
        await _authStore.DeleteAsync(cancellationToken);
        await SetAuthConnectionAsync(false, cancellationToken);
        await Plan.SetAsync("Token required", cancellationToken);
        await RateLimitStatus.SetAsync("계정 연결 후 확인", cancellationToken);
        await CreditStatus.SetAsync("계정 연결 후 확인", cancellationToken);
        await ApplyWindowAsync(null, FiveHourLimit, FiveHourLimitTimeText, FiveHourUsedPercent, cancellationToken);
        await ApplyWindowAsync(null, WeeklyLimit, WeeklyLimitTimeText, WeeklyUsedPercent, cancellationToken);
        await Status.SetAsync("저장된 토큰을 삭제했습니다", cancellationToken);
        _toastModel.Show("저장된 계정 정보를 삭제했습니다.", ToastSeverity.Info);
    }

    public async ValueTask Refresh(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        RestartRefreshDelay();
    }

    private async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        if (!DashboardRuntimeSettings.UseCodex)
        {
            await NextRefreshText.SetAsync("Codex 사용 안 함", cancellationToken);
            await Status.SetAsync("설정에서 Codex가 꺼져 있습니다", cancellationToken);
            return;
        }

        await NextRefreshText.SetAsync("리프레쉬 중...", cancellationToken);

        var tokens = await _authStore.LoadAsync(cancellationToken);
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            await SetAuthConnectionAsync(false, cancellationToken);
            await Status.SetAsync("AppData\\Local\\TokenDock\\auth.json에 토큰을 저장하세요", cancellationToken);
            return;
        }

        await SetAuthConnectionAsync(true, cancellationToken);

        try
        {
            var usage = await _openAiUsageService.GetUsageAsync(
                tokens.AccessToken,
                tokens.ChatGptAccountId,
                cancellationToken);

            await Plan.SetAsync(usage.PlanType.ToUpperInvariant(), cancellationToken);
            await ApplyWindowAsync(
                usage.FiveHourLimit,
                FiveHourLimit,
                FiveHourLimitTimeText,
                FiveHourUsedPercent,
                cancellationToken);
            await ApplyWindowAsync(
                usage.WeeklyLimit,
                WeeklyLimit,
                WeeklyLimitTimeText,
                WeeklyUsedPercent,
                cancellationToken);
            await RateLimitStatus.SetAsync(FormatRateLimitStatus(usage), cancellationToken);
            await CreditStatus.SetAsync(FormatCreditStatus(usage.Credits), cancellationToken);
            await Status.SetAsync($"마지막 확인 {DateTime.Now:HH:mm:ss}", cancellationToken);

            foreach (var alert in _usageAlertTracker.Evaluate(usage))
            {
                _toastModel.Show(alert.Message, alert.Severity);
            }
        }
        catch (Exception ex)
        {
            var message = OpenAiUsageErrorFormatter.Format(ex);
            await Status.SetAsync(message, cancellationToken);
            _toastModel.Show(message, GetErrorSeverity(ex));
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (DashboardRuntimeSettings.UseCodex)
                {
                    await RefreshAsync(cancellationToken);
                }
                else
                {
                    await NextRefreshText.SetAsync("Codex 사용 안 함", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                var message = OpenAiUsageErrorFormatter.Format(ex);
                await Status.SetAsync(message, cancellationToken);
                _toastModel.Show(message, GetErrorSeverity(ex));
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
            if (DashboardRuntimeSettings.UseCodex)
            {
                await NextRefreshText.SetAsync($"다음 리프레쉬 {remaining}초 후", cancellationToken);
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

    private async ValueTask SetAuthConnectionAsync(bool isConnected, CancellationToken cancellationToken)
    {
        await AuthConnection.SetAsync(new AuthConnectionState(isConnected), cancellationToken);
    }

    private static async ValueTask ApplyWindowAsync(
        OpenAiUsageWindow? window,
        IState<string> remainingText,
        IState<string> resetTimeText,
        IState<string> usedPercent,
        CancellationToken cancellationToken)
    {
        if (window is null)
        {
            await remainingText.SetAsync("--% 남음", cancellationToken);
            await resetTimeText.SetAsync("리셋 시간 없음", cancellationToken);
            await usedPercent.SetAsync("0", cancellationToken);
            return;
        }

        var used = Math.Clamp(window.UsedPercent, 0, 100);
        var remaining = 100 - used;
        var localReset = window.ResetsAt.ToOffset(TimeSpan.FromHours(9)).DateTime;

        await remainingText.SetAsync($"{remaining}% 남음", cancellationToken);
        var remainingResetTime = FormatRemainingResetTime(localReset);

        await resetTimeText.SetAsync($"{remainingResetTime} 후 리셋", cancellationToken);
        await usedPercent.SetAsync(used.ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    private static string FormatRemainingResetTime(DateTime resetTime)
    {
        var now = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(9)).DateTime;
        var remainingMinutes = Math.Max(0, (int)Math.Ceiling((resetTime - now).TotalMinutes));

        if (remainingMinutes == 0)
        {
            return "0시간 00분";
        }

        var days = remainingMinutes / (24 * 60);
        var hours = remainingMinutes % (24 * 60) / 60;
        var minutes = remainingMinutes % 60;

        return days > 0
            ? $"{days}일 {hours}시간 {minutes:00}분"
            : $"{hours}시간 {minutes:00}분";
    }

    private static string FormatRateLimitStatus(OpenAiUsageSnapshot usage)
    {
        return string.IsNullOrWhiteSpace(usage.RateLimitReachedType)
            ? "제한 없음"
            : "사용 제한 중";
    }

    private static string FormatCreditStatus(OpenAiCreditsSnapshot? credits)
    {
        if (credits is null)
        {
            return "정보 없음";
        }

        if (credits.Unlimited)
        {
            return "무제한";
        }

        if (!credits.HasCredits)
        {
            return "크레딧 없음";
        }

        return string.IsNullOrWhiteSpace(credits.Balance)
            ? "사용 가능"
            : credits.Balance;
    }

    private static ToastSeverity GetErrorSeverity(Exception exception)
    {
        return exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden }
            or System.Text.Json.JsonException
            ? ToastSeverity.Danger
            : ToastSeverity.Warning;
    }
}

public sealed record AuthConnectionState(bool IsConnected)
{
    public bool IsDisconnected => !IsConnected;
}

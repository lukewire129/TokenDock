using System;
using TokenDock.Services;

namespace TokenDock.Tests;

public sealed class CodexUsageAlertTrackerTests
{
    [Fact]
    public void Evaluate_ReturnsWarningWhenFiveHourLimitDropsBelowTwentyPercent()
    {
        var tracker = new CodexUsageAlertTracker();
        var usage = CreateUsage(fiveHourUsedPercent: 81, weeklyUsedPercent: 50);

        var alert = Assert.Single(tracker.Evaluate(usage));

        Assert.Equal("five-hour", alert.LimitKey);
        Assert.Equal("5시간 한도 20% 남았습니다.", alert.Message);
        Assert.Equal(ToastSeverity.Warning, alert.Severity);
    }

    [Fact]
    public void Evaluate_ReturnsDangerWhenWeeklyLimitDropsBelowFivePercent()
    {
        var tracker = new CodexUsageAlertTracker();
        var usage = CreateUsage(fiveHourUsedPercent: 50, weeklyUsedPercent: 96);

        var alert = Assert.Single(tracker.Evaluate(usage));

        Assert.Equal("weekly", alert.LimitKey);
        Assert.Equal("주간 한도 5% 남았습니다. 중요한 작업은 저장해두세요.", alert.Message);
        Assert.Equal(ToastSeverity.Danger, alert.Severity);
    }

    [Fact]
    public void Evaluate_DoesNotRepeatSameThresholdUntilResetChanges()
    {
        var tracker = new CodexUsageAlertTracker();
        var resetAt = DateTimeOffset.FromUnixTimeSeconds(1710000000);

        Assert.Single(tracker.Evaluate(CreateUsage(91, 50, fiveHourResetAt: resetAt)));
        Assert.Empty(tracker.Evaluate(CreateUsage(92, 50, fiveHourResetAt: resetAt)));
        Assert.Single(tracker.Evaluate(CreateUsage(92, 50, fiveHourResetAt: resetAt.AddHours(5))));
    }

    private static OpenAiUsageSnapshot CreateUsage(
        int fiveHourUsedPercent,
        int weeklyUsedPercent,
        DateTimeOffset? fiveHourResetAt = null,
        DateTimeOffset? weeklyResetAt = null)
    {
        return new OpenAiUsageSnapshot(
            PlanType: "pro",
            FiveHourLimit: new OpenAiUsageWindow(
                fiveHourUsedPercent,
                TimeSpan.FromHours(5),
                fiveHourResetAt ?? DateTimeOffset.FromUnixTimeSeconds(1710000000)),
            WeeklyLimit: new OpenAiUsageWindow(
                weeklyUsedPercent,
                TimeSpan.FromDays(7),
                weeklyResetAt ?? DateTimeOffset.FromUnixTimeSeconds(1710604800)),
            Credits: null,
            RateLimitReachedType: null,
            AdditionalLimits: Array.Empty<OpenAiAdditionalUsageLimit>());
    }
}

using System;
using System.Collections.Generic;
using TokenDock.Services;

namespace TokenDock.Tests;

public sealed class ClaudeUsageAlertTrackerTests
{
    [Fact]
    public void Evaluate_ReturnsWarningWhenSessionLimitDropsBelowTwentyPercent()
    {
        var tracker = new ClaudeUsageAlertTracker();
        var usage = CreateUsage(sessionUtilization: 80.1, weeklyUtilization: 50);

        var alert = Assert.Single(tracker.Evaluate(usage));

        Assert.Equal("session", alert.LimitKey);
        Assert.Equal("Claude 5시간 한도 20% 남았습니다.", alert.Message);
        Assert.Equal(ToastSeverity.Warning, alert.Severity);
    }

    [Fact]
    public void Evaluate_ReturnsDangerWhenWeeklyLimitDropsBelowFivePercent()
    {
        var tracker = new ClaudeUsageAlertTracker();
        var usage = CreateUsage(sessionUtilization: 50, weeklyUtilization: 95.1);

        var alert = Assert.Single(tracker.Evaluate(usage));

        Assert.Equal("weekly", alert.LimitKey);
        Assert.Equal("Claude 주간 한도 5% 남았습니다. 중요한 작업은 저장해두세요.", alert.Message);
        Assert.Equal(ToastSeverity.Danger, alert.Severity);
    }

    [Fact]
    public void Evaluate_DoesNotRepeatSameThresholdUntilResetChanges()
    {
        var tracker = new ClaudeUsageAlertTracker();
        var resetAt = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);

        Assert.Single(tracker.Evaluate(CreateUsage(91, 50, sessionResetAt: resetAt)));
        Assert.Empty(tracker.Evaluate(CreateUsage(92, 50, sessionResetAt: resetAt)));
        Assert.Single(tracker.Evaluate(CreateUsage(92, 50, sessionResetAt: resetAt.AddHours(5))));
    }

    private static ClaudeUsageSnapshot CreateUsage(
        double sessionUtilization,
        double weeklyUtilization,
        DateTime? sessionResetAt = null,
        DateTime? weeklyResetAt = null)
    {
        return new ClaudeUsageSnapshot(
            Session: new ClaudeRateWindow(sessionUtilization, 300, sessionResetAt ?? new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc), "1시간"),
            Weekly: new ClaudeRateWindow(weeklyUtilization, 10080, weeklyResetAt ?? new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc), "3일"),
            ModelWindows: new Dictionary<string, ClaudeRateWindow>(),
            Plan: "pro",
            Email: null,
            UpdatedAt: DateTime.UtcNow);
    }
}

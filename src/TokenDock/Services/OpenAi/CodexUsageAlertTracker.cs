using System;
using System.Collections.Generic;

namespace TokenDock.Services;

public sealed class CodexUsageAlertTracker
{
    private static readonly int[] Thresholds = [20, 10, 5];

    private readonly Dictionary<string, UsageLimitAlertState> _states = new(StringComparer.Ordinal);

    public IReadOnlyList<CodexUsageAlert> Evaluate(OpenAiUsageSnapshot usage)
    {
        var alerts = new List<CodexUsageAlert>(capacity: 2);
        AddAlertIfNeeded(alerts, "five-hour", "5시간 한도", usage.FiveHourLimit);
        AddAlertIfNeeded(alerts, "weekly", "주간 한도", usage.WeeklyLimit);
        return alerts;
    }

    private void AddAlertIfNeeded(List<CodexUsageAlert> alerts, string key, string label, OpenAiUsageWindow? window)
    {
        if (window is null)
        {
            _states.Remove(key);
            return;
        }

        var state = _states.TryGetValue(key, out var existing) && existing.ResetAt == window.ResetsAt
            ? existing
            : new UsageLimitAlertState(window.ResetsAt, new HashSet<int>());

        var remaining = 100 - Math.Clamp(window.UsedPercent, 0, 100);
        var threshold = GetCurrentThreshold(remaining);
        if (threshold is null)
        {
            _states[key] = state;
            return;
        }

        var alreadyNotified = existingThresholdWasAlreadyNotified(existing, window.ResetsAt, threshold.Value);

        foreach (var candidate in Thresholds)
        {
            if (remaining <= candidate)
            {
                state.NotifiedThresholds.Add(candidate);
            }
        }

        if (!alreadyNotified)
        {
            alerts.Add(new CodexUsageAlert(
                LimitKey: key,
                Message: BuildMessage(label, threshold.Value),
                Severity: threshold.Value <= 5 ? ToastSeverity.Danger : ToastSeverity.Warning));
        }

        _states[key] = state;

        static bool existingThresholdWasAlreadyNotified(
            UsageLimitAlertState? existing,
            DateTimeOffset resetAt,
            int threshold)
        {
            return existing is not null
                && existing.ResetAt == resetAt
                && existing.NotifiedThresholds.Contains(threshold);
        }
    }

    private static int? GetCurrentThreshold(int remaining)
    {
        if (remaining <= 5)
        {
            return 5;
        }

        if (remaining <= 10)
        {
            return 10;
        }

        if (remaining <= 20)
        {
            return 20;
        }

        return null;
    }

    private static string BuildMessage(string label, int threshold)
    {
        return threshold <= 5
            ? $"{label} {threshold}% 남았습니다. 중요한 작업은 저장해두세요."
            : $"{label} {threshold}% 남았습니다.";
    }

    private sealed record UsageLimitAlertState(
        DateTimeOffset ResetAt,
        HashSet<int> NotifiedThresholds);
}

public sealed record CodexUsageAlert(
    string LimitKey,
    string Message,
    ToastSeverity Severity);

public enum ToastSeverity
{
    Info,
    Warning,
    Danger
}

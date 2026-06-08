using System;
using System.Collections.Generic;

namespace TokenDock.Services;

public sealed class ClaudeUsageAlertTracker
{
    private static readonly int[] Thresholds = [20, 10, 5];

    private readonly Dictionary<string, UsageLimitAlertState> _states = new(StringComparer.Ordinal);

    public IReadOnlyList<ClaudeUsageAlert> Evaluate(ClaudeUsageSnapshot usage)
    {
        var alerts = new List<ClaudeUsageAlert>(capacity: 2);
        AddAlertIfNeeded(alerts, "session", "Claude 5시간 한도", usage.Session);
        AddAlertIfNeeded(alerts, "weekly", "Claude 주간 한도", usage.Weekly);
        return alerts;
    }

    private void AddAlertIfNeeded(List<ClaudeUsageAlert> alerts, string key, string label, ClaudeRateWindow? window)
    {
        if (window is null)
        {
            _states.Remove(key);
            return;
        }

        var resetAt = window.ResetsAtUtc;
        var state = _states.TryGetValue(key, out var existing) && existing.ResetAt == resetAt
            ? existing
            : new UsageLimitAlertState(resetAt, new HashSet<int>());

        var remaining = 100 - (int)Math.Ceiling(Math.Clamp(window.Utilization, 0, 100));
        var threshold = GetCurrentThreshold(remaining);
        if (threshold is null)
        {
            _states[key] = state;
            return;
        }

        var alreadyNotified = existing is not null
            && existing.ResetAt == resetAt
            && existing.NotifiedThresholds.Contains(threshold.Value);

        foreach (var candidate in Thresholds)
        {
            if (remaining <= candidate)
            {
                state.NotifiedThresholds.Add(candidate);
            }
        }

        if (!alreadyNotified)
        {
            alerts.Add(new ClaudeUsageAlert(
                LimitKey: key,
                Message: BuildMessage(label, threshold.Value),
                Severity: threshold.Value <= 5 ? ToastSeverity.Danger : ToastSeverity.Warning));
        }

        _states[key] = state;
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
        DateTime? ResetAt,
        HashSet<int> NotifiedThresholds);
}

public sealed record ClaudeUsageAlert(
    string LimitKey,
    string Message,
    ToastSeverity Severity);

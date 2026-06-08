using Luke.Mvux;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace TokenDock;

public partial record MiniStatusWidgetModel
{
    public MiniStatusWidgetModel(
        CodexDashboardViewModel codexDashboard,
        ClaudeDashboardViewModel claudeDashboard,
        SettingsViewModel settings)
    {
        CodexDashboard = codexDashboard;
        ClaudeDashboard = claudeDashboard;
        Settings = settings;

        if (CodexDashboard is INotifyPropertyChanged codexNotify)
        {
            codexNotify.PropertyChanged += (_, _) => _ = RefreshSnapshotAsync(CancellationToken.None);
        }

        if (ClaudeDashboard is INotifyPropertyChanged claudeNotify)
        {
            claudeNotify.PropertyChanged += (_, _) => _ = RefreshSnapshotAsync(CancellationToken.None);
        }

        Settings.PropertyChanged += (_, e) =>
        {
            if (IsWidgetSettingProperty(e.PropertyName))
            {
                _ = RefreshSnapshotAsync(CancellationToken.None);
            }
        };

        _ = RefreshSnapshotAsync(CancellationToken.None);
    }

    public CodexDashboardViewModel CodexDashboard { get; }

    public ClaudeDashboardViewModel ClaudeDashboard { get; }

    public SettingsViewModel Settings { get; }

    public IState<MiniStatusWidgetSnapshot> Snapshot => State.Value(this, () => MiniStatusWidgetSnapshot.Default);

    private async ValueTask RefreshSnapshotAsync(CancellationToken cancellationToken)
    {
        await Snapshot.SetAsync(CreateSnapshot(), cancellationToken);
    }

    private MiniStatusWidgetSnapshot CreateSnapshot()
    {
        var currentSettings = CurrentSettings;
        var effectiveTarget = EffectiveTarget;
        var isGlassMode = currentSettings.WidgetMode == WidgetMode.Glass;
        var isCompactMode = currentSettings.WidgetMode == WidgetMode.Compact;
        var isGaugeMode = currentSettings.WidgetMode == WidgetMode.Gauge;

        return new MiniStatusWidgetSnapshot(
            currentSettings.WidgetOpacity,
            currentSettings.IsWidgetAlwaysOnTop,
            effectiveTarget,
            Title,
            Subtitle,
            PrimaryLabel,
            PrimaryLimit,
            PrimaryLimit2,
            PrimaryUsedPercent,
            PrimaryUsedPercent2,
            SecondaryLabel,
            SecondaryLimit,
            SecondaryLimit2,
            SecondaryUsedPercent,
            SecondaryUsedPercent2,
            NextRefreshText,
            CompactText,
            CompactExtendedText,
            isGlassMode,
            isGlassMode && effectiveTarget != EffectiveWidgetTarget.Combined,
            isGlassMode && effectiveTarget == EffectiveWidgetTarget.Combined,
            isCompactMode,
            isCompactMode && effectiveTarget == EffectiveWidgetTarget.Codex,
            isCompactMode && effectiveTarget == EffectiveWidgetTarget.Claude,
            isCompactMode && effectiveTarget == EffectiveWidgetTarget.Combined,
            isGaugeMode,
            isGaugeMode && effectiveTarget != EffectiveWidgetTarget.Combined,
            isGaugeMode && effectiveTarget == EffectiveWidgetTarget.Combined);
    }

    public EffectiveWidgetTarget EffectiveTarget => ResolveTarget(Settings);

    public string Title => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => "Claude Usage",
        EffectiveWidgetTarget.Combined => "AI Usage",
        _ => "TokenDock"
    };

    public string Subtitle => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => ClaudeDashboard.Plan ?? "Claude",
        EffectiveWidgetTarget.Combined => $"Codex ({CodexDashboard.Plan})  /  Claude ({ClaudeDashboard.Plan})",
        _ => CodexDashboard.Plan ?? "Codex"
    };

    public string PrimaryLabel => EffectiveTarget == EffectiveWidgetTarget.Combined
        ? "Codex"
        : "5시간";

    public string PrimaryLimit => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => ClaudeDashboard.SessionLimit ?? "--% 남음",
        EffectiveWidgetTarget.Combined => $"5h {CodexDashboard.FiveHourLimit}",
        _ => CodexDashboard.FiveHourLimit ?? "--% 남음"
    };

    public string PrimaryLimit2 => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => ClaudeDashboard.SessionLimit ?? "--% 남음",
        EffectiveWidgetTarget.Combined => $"7d {CodexDashboard.WeeklyLimit}",
        _ => CodexDashboard.FiveHourLimit ?? "--% 남음"
    };

    public string PrimaryUsedPercent => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => ClaudeDashboard.SessionUsedPercent ?? "0",
        _ => CodexDashboard.FiveHourUsedPercent ?? "0"
    };

    public string PrimaryUsedPercent2 => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => ClaudeDashboard.SessionUsedPercent ?? "0",
        _ => CodexDashboard.WeeklyUsedPercent ?? "0"
    };

    public string SecondaryLabel => EffectiveTarget == EffectiveWidgetTarget.Combined
        ? "Claude"
        : "주간";

    public string SecondaryLimit => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => ClaudeDashboard.WeeklyLimit ?? "--% 남음",
        EffectiveWidgetTarget.Combined => $"5d {ClaudeDashboard.SessionLimit}",
        _ => CodexDashboard.WeeklyLimit ?? "--% 남음"
    };

    public string SecondaryLimit2 => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => ClaudeDashboard.WeeklyLimit ?? "--% 남음",
        EffectiveWidgetTarget.Combined => $"7d {ClaudeDashboard.WeeklyLimit}",
        _ => CodexDashboard.WeeklyLimit ?? "--% 남음"
    };

    public string SecondaryUsedPercent => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => ClaudeDashboard.WeeklyUsedPercent ?? "0",
        EffectiveWidgetTarget.Combined => ClaudeDashboard.SessionUsedPercent ?? "0",
        _ => CodexDashboard.WeeklyUsedPercent ?? "0"
    };

    public string SecondaryUsedPercent2 => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => ClaudeDashboard.WeeklyUsedPercent ?? "0",
        EffectiveWidgetTarget.Combined => ClaudeDashboard.WeeklyUsedPercent ?? "0",
        _ => CodexDashboard.WeeklyUsedPercent ?? "0"
    };

    public string NextRefreshText => EffectiveTarget switch
    {
        EffectiveWidgetTarget.Claude => ClaudeDashboard.NextRefreshText ?? "Claude 사용량 조회 대기",
        EffectiveWidgetTarget.Combined => $"Codex {CodexDashboard.NextRefreshText} · Claude {ClaudeDashboard.NextRefreshText}",
        _ => CodexDashboard.NextRefreshText ?? "Codex 사용량 조회 대기"
    };

    public string CompactText => EffectiveTarget == EffectiveWidgetTarget.Combined
        ? FormatCompactGroup(ClaudeDashboard.SessionLimit, ClaudeDashboard.WeeklyLimit)
        : FormatCompactGroup(CodexDashboard.FiveHourLimit, CodexDashboard.WeeklyLimit);

    public string CompactExtendedText => FormatCompactGroup(ClaudeDashboard.SessionLimit, ClaudeDashboard.WeeklyLimit);

    public bool IsGlassMode => CurrentSettings.WidgetMode == WidgetMode.Glass;

    public bool IsSingleGlassMode => IsGlassMode && EffectiveTarget != EffectiveWidgetTarget.Combined;

    public bool IsCombinedGlassMode => IsGlassMode && EffectiveTarget == EffectiveWidgetTarget.Combined;

    public bool IsCompactMode => CurrentSettings.WidgetMode == WidgetMode.Compact;

    public bool IsSingleCodexCompactMode => IsCompactMode && EffectiveTarget == EffectiveWidgetTarget.Codex;

    public bool IsSingleClaudeCompactMode => IsCompactMode && EffectiveTarget == EffectiveWidgetTarget.Claude;

    public bool IsCombinedCompactMode => IsCompactMode && EffectiveTarget == EffectiveWidgetTarget.Combined;

    public bool IsGaugeMode => CurrentSettings.WidgetMode == WidgetMode.Gauge;

    public bool IsSingleGaugeMode => IsGaugeMode && EffectiveTarget != EffectiveWidgetTarget.Combined;

    public bool IsCombinedGaugeMode => IsGaugeMode && EffectiveTarget == EffectiveWidgetTarget.Combined;

    public static EffectiveWidgetTarget ResolveTarget(SettingsViewModel settings)
    {
        var snapshot = settings.Settings ?? SettingsSnapshot.Default;
        return snapshot.WidgetTarget switch
        {
            WidgetTarget.Codex => EffectiveWidgetTarget.Codex,
            WidgetTarget.Claude => EffectiveWidgetTarget.Claude,
            WidgetTarget.Combined => EffectiveWidgetTarget.Combined,
            _ when snapshot.UseCodex && snapshot.UseClaude => EffectiveWidgetTarget.Combined,
            _ when snapshot.UseClaude => EffectiveWidgetTarget.Claude,
            _ => EffectiveWidgetTarget.Codex
        };
    }

    private static string FormatCompactGroup(string? fiveHour, string? weekly)
    {
        return $"5h {FormatCompactPercent(fiveHour)} / 7d {FormatCompactPercent(weekly)}";
    }

    private static string FormatCompactPercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "--%";
        }

        return value.Replace(" 남음", string.Empty, StringComparison.Ordinal);
    }

    private SettingsSnapshot CurrentSettings => Settings.Settings ?? SettingsSnapshot.Default;

    private static bool IsWidgetSettingProperty(string? propertyName)
    {
        return propertyName is nameof(SettingsViewModel.Settings);
    }
}

public enum EffectiveWidgetTarget
{
    Codex,
    Claude,
    Combined
}

public sealed record MiniStatusWidgetSnapshot(
    double WidgetOpacity,
    bool IsWidgetAlwaysOnTop,
    EffectiveWidgetTarget EffectiveTarget,
    string Title,
    string Subtitle,
    string PrimaryLabel,
    string PrimaryLimit,
    string PrimaryLimit2,
    string PrimaryUsedPercent,
    string PrimaryUsedPercent2,
    string SecondaryLabel,
    string SecondaryLimit,
    string SecondaryLimit2,
    string SecondaryUsedPercent,
    string SecondaryUsedPercent2,
    string NextRefreshText,
    string CompactText,
    string CompactExtendedText,
    bool IsGlassMode,
    bool IsSingleGlassMode,
    bool IsCombinedGlassMode,
    bool IsCompactMode,
    bool IsSingleCodexCompactMode,
    bool IsSingleClaudeCompactMode,
    bool IsCombinedCompactMode,
    bool IsGaugeMode,
    bool IsSingleGaugeMode,
    bool IsCombinedGaugeMode)
{
    public static MiniStatusWidgetSnapshot Default { get; } = new(
        SettingsSnapshot.Default.WidgetOpacity,
        SettingsSnapshot.Default.IsWidgetAlwaysOnTop,
        EffectiveWidgetTarget.Combined,
        "AI Usage",
        "Codex (Token required)  /  Claude (Claude)",
        "Codex",
        "5h --% 남음",
        "7d --% 남음",
        "0",
        "0",
        "Claude",
        "5d --% 남음",
        "7d --% 남음",
        "0",
        "0",
        "Codex 다음 리프레쉬 --초 후 · Claude Claude 사용량 조회 미연결",
        "5h --% / 7d --%",
        "5h --% / 7d --%",
        true,
        false,
        true,
        false,
        false,
        false,
        false,
        false,
        false,
        false);
}

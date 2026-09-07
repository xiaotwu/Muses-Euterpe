using Localization = global::Muses.Core.L10n.L10n;

namespace Muses.Core.Advanced;

public enum FocusStatus
{
    Active,
    Completed,
    Cancelled
}

public enum FocusExpiration
{
    KeepPlaying,
    Pause,
    NotifyOnly
}

public static class FocusExpirationExtensions
{
    public static string GetLabel(this FocusExpiration expiration) => expiration switch
    {
        FocusExpiration.KeepPlaying => Localization.Tr("Keep Playing", "继续播放"),
        FocusExpiration.Pause => Localization.Tr("Pause", "暂停"),
        FocusExpiration.NotifyOnly => Localization.Tr("Notify Only", "仅通知"),
        _ => expiration.ToString()
    };
}

public enum FocusPomodoroPhase
{
    Focus,
    Break
}

public sealed record FocusSessionEntity(
    string Id,
    long StartedAt,
    int? PlannedDurationMs,
    long? EndedAt,
    FocusStatus Status,
    string? ListeningSessionId = null);

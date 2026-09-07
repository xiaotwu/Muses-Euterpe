using System.Text.Json;
using Muses.Core.Domain;
using Localization = global::Muses.Core.L10n.L10n;

namespace Muses.Core.Advanced;

public enum AutomationTrigger
{
    TrackStarted,
    TrackCompleted,
    TrackSkipped
}

public static class AutomationTriggerExtensions
{
    public static string GetLabel(this AutomationTrigger trigger) => trigger switch
    {
        AutomationTrigger.TrackStarted => Localization.Tr("When a track starts", "曲目开始时"),
        AutomationTrigger.TrackCompleted => Localization.Tr("When a track completes", "曲目播完时"),
        AutomationTrigger.TrackSkipped => Localization.Tr("When a track is skipped", "曲目被跳过时"),
        _ => trigger.ToString()
    };
}

public enum AutomationAction
{
    LikeTrack,
    AddToInbox,
    PlayNext,
    AddToQueue
}

public static class AutomationActionExtensions
{
    public static string GetLabel(this AutomationAction action) => action switch
    {
        AutomationAction.LikeTrack => Localization.Tr("Like track", "收藏曲目"),
        AutomationAction.AddToInbox => Localization.Tr("Add to Inbox", "加入收件箱"),
        AutomationAction.PlayNext => Localization.Tr("Play next", "下一首播放"),
        AutomationAction.AddToQueue => Localization.Tr("Add to queue", "加入队列"),
        _ => action.ToString()
    };
}

public sealed record AutomationConditions(
    string? AppBundleId = null,
    TimeBand? TimeBand = null,
    bool? IsHeadphones = null,
    bool? IsWeekend = null);

public sealed record AutomationRuleEntity(
    string Id,
    string Name,
    bool Enabled,
    AutomationTrigger Trigger,
    string? ConditionsJson,
    AutomationAction Action,
    int? CooldownMs = null,
    long? LastFiredAt = null)
{
    public AutomationConditions? GetConditions()
    {
        if (string.IsNullOrWhiteSpace(ConditionsJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<AutomationConditions>(ConditionsJson);
        }
        catch
        {
            return null;
        }
    }

    public static AutomationRuleEntity Create(
        string name,
        AutomationTrigger trigger,
        AutomationConditions? conditions,
        AutomationAction action,
        int? cooldownMs = null)
    {
        var condJson = conditions != null ? JsonSerializer.Serialize(conditions) : null;
        return new AutomationRuleEntity(
            Guid.NewGuid().ToString(),
            name,
            true,
            trigger,
            condJson,
            action,
            cooldownMs,
            null);
    }
}

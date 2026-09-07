using Muses.Core.Advanced;
using Muses.Core.Domain;
using Muses.Core.Playback;

namespace Muses.Infrastructure.Advanced;

public sealed class AutomationService : IDisposable
{
    private readonly IAutomationRepository _repository;
    private readonly PlaybackService _playback;
    private readonly Func<ListeningContext?> _contextProvider;
    private readonly Action<AutomationAction, TrackSnapshot> _actionHandler;
    private bool _isDispatching;

    public bool IsEnabled { get; set; } = true;
    public event Action? Changed;

    public AutomationService(
        IAutomationRepository repository,
        PlaybackService playback,
        Func<ListeningContext?>? contextProvider = null,
        Action<AutomationAction, TrackSnapshot>? actionHandler = null)
    {
        _repository = repository;
        _playback = playback;
        _contextProvider = contextProvider ?? (() => null);
        _actionHandler = actionHandler ?? ((_, _) => { });

        _playback.EventBus.EventPosted += HandleEvent;
    }

    public IReadOnlyList<AutomationRuleEntity> GetRules() => _repository.GetRules();

    public AutomationRuleEntity AddRule(
        string name,
        AutomationTrigger trigger,
        AutomationConditions? conditions,
        AutomationAction action,
        int? cooldownMs = null)
    {
        var rule = AutomationRuleEntity.Create(name, trigger, conditions, action, cooldownMs);
        _repository.SaveRule(rule);
        Changed?.Invoke();
        return rule;
    }

    public void RemoveRule(string id)
    {
        _repository.DeleteRule(id);
        Changed?.Invoke();
    }

    public void SetEnabled(string id, bool enabled)
    {
        _repository.SetEnabled(id, enabled);
        Changed?.Invoke();
    }

    private void HandleEvent(PlaybackEvent evt)
    {
        if (_isDispatching || !IsEnabled) return;

        var trigger = MapTrigger(evt);
        var snapshot = evt.Track;
        if (trigger == null || snapshot == null) return;

        var rules = _repository.GetRules().Where(r => r.Enabled && r.Trigger == trigger.Value).ToList();
        if (rules.Count == 0) return;

        _isDispatching = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var context = _contextProvider();

            foreach (var rule in rules)
            {
                if (!Matches(rule.GetConditions(), context)) continue;
                if (!CooldownAllows(rule, now.ToUnixTimeMilliseconds())) continue;

                _actionHandler(rule.Action, snapshot);
                _repository.RecordFire(rule.Id, now.ToUnixTimeMilliseconds());
                Changed?.Invoke();
            }
        }
        finally
        {
            _isDispatching = false;
        }
    }

    public static bool Matches(AutomationConditions? conditions, ListeningContext? context)
    {
        if (conditions == null) return true;
        if (conditions.AppBundleId != null && context?.FrontmostAppBundleId != conditions.AppBundleId)
            return false;
        if (conditions.TimeBand.HasValue && context?.TimeBand != conditions.TimeBand.Value)
            return false;
        if (conditions.IsHeadphones.HasValue && context?.IsHeadphones != conditions.IsHeadphones.Value)
            return false;
        if (conditions.IsWeekend.HasValue && context?.IsWeekend != conditions.IsWeekend.Value)
            return false;
        return true;
    }

    public static bool CooldownAllows(AutomationRuleEntity rule, long nowMs)
    {
        if (!rule.CooldownMs.HasValue || !rule.LastFiredAt.HasValue) return true;
        return (nowMs - rule.LastFiredAt.Value) >= rule.CooldownMs.Value;
    }

    private static AutomationTrigger? MapTrigger(PlaybackEvent evt) => evt.Kind switch
    {
        PlaybackEventKind.TrackStarted => AutomationTrigger.TrackStarted,
        PlaybackEventKind.TrackCompleted => AutomationTrigger.TrackCompleted,
        PlaybackEventKind.TrackSkipped => AutomationTrigger.TrackSkipped,
        _ => null
    };

    public void Dispose()
    {
        _playback.EventBus.EventPosted -= HandleEvent;
    }
}

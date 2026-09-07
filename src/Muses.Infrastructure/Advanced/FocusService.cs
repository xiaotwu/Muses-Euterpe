using Muses.Core.Advanced;
using Muses.Core.Playback;

namespace Muses.Infrastructure.Advanced;

public sealed class FocusService : IDisposable
{
    private readonly IFocusRepository _repository;
    private readonly PlaybackService _playback;
    private CancellationTokenSource? _timerCts;

    public bool IsActive { get; private set; }
    public bool IsQueueLocked { get; private set; }
    public double RemainingSeconds { get; private set; }
    public double TotalSeconds { get; private set; }
    public FocusExpiration Expiration { get; private set; } = FocusExpiration.Pause;
    public bool IsPomodoro { get; private set; }
    public FocusPomodoroPhase PomodoroPhase { get; private set; } = FocusPomodoroPhase.Focus;
    public string? ActiveSessionId { get; private set; }

    public event Action? StateChanged;
    public event Action? Expired;

    public FocusService(IFocusRepository repository, PlaybackService playback)
    {
        _repository = repository;
        _playback = playback;
    }

    public string RemainingFormatted
    {
        get
        {
            var total = (int)Math.Max(0, RemainingSeconds);
            var h = total / 3600;
            var m = (total % 3600) / 60;
            var s = total % 60;
            return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m}:{s:D2}";
        }
    }

    public void Start(int? minutes, bool queueLocked = false, FocusExpiration expiration = FocusExpiration.Pause, bool pomodoro = false)
    {
        StopTimer();
        IsPomodoro = pomodoro;
        IsQueueLocked = queueLocked;
        Expiration = expiration;
        IsActive = true;

        int? plannedMs;
        double totalSec;

        if (pomodoro)
        {
            PomodoroPhase = FocusPomodoroPhase.Focus;
            totalSec = 25 * 60;
            plannedMs = 25 * 60 * 1000;
        }
        else if (minutes.HasValue)
        {
            totalSec = minutes.Value * 60;
            plannedMs = minutes.Value * 60 * 1000;
        }
        else
        {
            totalSec = 0;
            plannedMs = null;
        }

        TotalSeconds = totalSec;
        RemainingSeconds = totalSec;

        var id = Guid.NewGuid().ToString();
        ActiveSessionId = id;
        var session = new FocusSessionEntity(
            id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            plannedMs,
            null,
            FocusStatus.Active);
        _repository.SaveSession(session);

        StartTimer();
        StateChanged?.Invoke();
    }

    public void Stop(bool completedByTimer = false)
    {
        if (!IsActive) return;
        StopTimer();
        IsActive = false;
        IsPomodoro = false;
        IsQueueLocked = false;
        RemainingSeconds = 0;
        TotalSeconds = 0;

        if (ActiveSessionId != null)
        {
            var session = new FocusSessionEntity(
                ActiveSessionId,
                0,
                null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                completedByTimer ? FocusStatus.Completed : FocusStatus.Cancelled);
            _repository.UpdateSession(session);
            ActiveSessionId = null;
        }

        StateChanged?.Invoke();
    }

    private void StartTimer()
    {
        if (TotalSeconds <= 0) return;
        _timerCts = new CancellationTokenSource();
        var token = _timerCts.Token;

        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (!token.IsCancellationRequested && RemainingSeconds > 0)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(token)) break;
                    RemainingSeconds--;
                    StateChanged?.Invoke();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (!token.IsCancellationRequested)
            {
                HandleExpiry();
            }
        }, token);
    }

    private void StopTimer()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
    }

    private void HandleExpiry()
    {
        if (!IsActive) return;

        if (IsPomodoro)
        {
            if (PomodoroPhase == FocusPomodoroPhase.Focus)
            {
                PomodoroPhase = FocusPomodoroPhase.Break;
                TotalSeconds = 5 * 60;
                RemainingSeconds = 5 * 60;
                ApplyExpiration();
                Expired?.Invoke();
                StartTimer();
                StateChanged?.Invoke();
                return;
            }
            else
            {
                PomodoroPhase = FocusPomodoroPhase.Focus;
                TotalSeconds = 25 * 60;
                RemainingSeconds = 25 * 60;
                ApplyExpiration();
                Expired?.Invoke();
                StartTimer();
                StateChanged?.Invoke();
                return;
            }
        }

        ApplyExpiration();
        Expired?.Invoke();
        Stop(completedByTimer: true);
    }

    private void ApplyExpiration()
    {
        if (Expiration == FocusExpiration.Pause)
        {
            _playback.Pause();
        }
    }

    public void Dispose()
    {
        StopTimer();
    }
}

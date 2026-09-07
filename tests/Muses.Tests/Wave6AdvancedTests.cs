using Muses.Core.Advanced;
using Muses.Core.Domain;
using Muses.Core.Playback;
using Muses.Core.Queue;
using Muses.Infrastructure.Advanced;
using Muses.Infrastructure.Playback;
using Muses.Persistence;
using Xunit;

namespace Muses.Tests;

public sealed class Wave6AdvancedTests
{
    [Fact]
    public void AudioInfoModel_BuildRows_HandlesNullAndFormatsValues()
    {
        var rowsNull = AudioInfoModel.BuildRows(null, null, null, 0.5);
        Assert.Contains(rowsNull, r => r.Label == "Codec" && r.Value == "Unknown");
        Assert.Contains(rowsNull, r => r.Label == "Volume" && r.Value == "50%");

        var track = new TrackSnapshot(
            Guid.NewGuid(), "Title", "Artist", "Album", 180, "ytid", null,
            SampleRate: 48000, BitDepth: 24, Codec: "opus", IsLossless: true,
            ReplayGain: -3.5, BitRate: 256000, Channels: 2);

        var rows = AudioInfoModel.BuildRows(track, "Realtek High Definition", "HiFi", 0.75);
        Assert.Contains(rows, r => r.Label == "Codec" && r.Value == "opus");
        Assert.Contains(rows, r => r.Label == "Lossless" && r.Value == "Yes");
        Assert.Contains(rows, r => r.Label == "Sample Rate" && r.Value == "48 kHz");
        Assert.Contains(rows, r => r.Label == "Bit Depth" && r.Value == "24-bit");
        Assert.Contains(rows, r => r.Label == "Bit Rate" && r.Value == "256 kbps");
        Assert.Contains(rows, r => r.Label == "Channels" && r.Value == "Stereo");
        Assert.Contains(rows, r => r.Label == "Output Device" && r.Value == "Realtek High Definition");
        Assert.Contains(rows, r => r.Label == "EQ Preset" && r.Value == "HiFi");
        Assert.Contains(rows, r => r.Label == "Volume" && r.Value == "75%");
    }

    [Fact]
    public void EQService_Presets_AndCustomSaveDelete_Work()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var eq = new EQService(store);

        Assert.Equal("Flat", eq.ActivePresetName);
        Assert.Equal(32, eq.ActiveBands.Count);
        Assert.All(eq.ActiveBands, b => Assert.Equal(0f, b.Gain));

        eq.SelectPreset("BassBoost");
        Assert.Equal("BassBoost", eq.ActivePresetName);
        Assert.True(eq.ActiveBands[0].Gain > 0);

        eq.SetBandGain(0, 10f);
        Assert.Equal("Custom", eq.ActivePresetName);
        Assert.Equal(10f, eq.ActiveBands[0].Gain);

        var saved = eq.SaveCustomPreset("MyProfile");
        Assert.Equal("MyProfile", eq.ActivePresetName);

        var presets = eq.GetCustomPresets();
        Assert.Single(presets);
        Assert.Equal("MyProfile", presets[0].Name);

        eq.DeleteCustomPreset(saved.Id);
        Assert.Empty(eq.GetCustomPresets());
        Assert.Equal("Flat", eq.ActivePresetName);
    }

    [Fact]
    public void NotesService_TrackNotesAndBookmarks_PerformCorrectly()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var notes = new NotesService(store);
        var trackId = Guid.NewGuid().ToString();

        // Note upsert
        Assert.Null(notes.GetNote(trackId));
        notes.SetTrackNote(trackId, "Guitar solo at 1:45");
        var n = notes.GetNote(trackId);
        Assert.NotNull(n);
        Assert.Equal("Guitar solo at 1:45", n.Content);

        // Bookmarks CRUD
        var b1 = notes.AddBookmark(trackId, 105000, "Solo start", "Epic transition");
        var b2 = notes.AddBookmark(trackId, 45000, "Verse 2", "Vocal harmonies");

        var bms = notes.GetBookmarks(trackId);
        Assert.Equal(2, bms.Count);
        // Sorted by timestamp ascending
        Assert.Equal(45000, bms[0].TimestampMs);
        Assert.Equal(105000, bms[1].TimestampMs);

        notes.UpdateBookmark(b1.Id, "Solo peak", "Guitar bend");
        var updated = notes.GetBookmarks(trackId).First(b => b.Id == b1.Id);
        Assert.Equal("Solo peak", updated.Title);
        Assert.Equal("Guitar bend", updated.Note);

        notes.RemoveBookmark(b2.Id);
        Assert.Single(notes.GetBookmarks(trackId));

        // Note Search
        var hits = notes.SearchNotes("solo", new Dictionary<string, string> { [trackId] = "Master of Puppets" });
        Assert.Single(hits);
        Assert.Equal("Master of Puppets", hits[0].TrackTitle);
        Assert.Contains("solo", hits[0].Snippet, StringComparison.OrdinalIgnoreCase);

        // Empty content deletes note
        notes.SetTrackNote(trackId, "   ");
        Assert.Null(notes.GetNote(trackId));
    }

    [Fact]
    public void InboxService_LifecycleAndListeningEvent_TransitionsState()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var engine = new FakePlayerEngine();
        var queue = new QueueService();
        var playback = new PlaybackService(engine, queue);
        using var inbox = new InboxService(store, playback);

        var snap = TrackSnapshot.Test("Cool Indie Track", "yt-inbox-1");
        var item = inbox.Add(snap, InboxSource.Manual);
        Assert.NotNull(item);
        Assert.Equal(InboxState.Unheard, item.State);

        // Deduplication: adding again returns null
        Assert.Null(inbox.Add(snap));

        // Track starts playing -> changes to Listening
        playback.EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackStarted, snap));
        var pending = inbox.GetPendingItems();
        var listeningItem = pending.First(i => i.Id == item.Id);
        Assert.Equal(InboxState.Listening, listeningItem.State);

        // Snooze
        var snoozeUntil = DateTimeOffset.UtcNow.AddHours(2);
        inbox.Snooze(item.Id, snoozeUntil);
        Assert.Empty(inbox.GetPendingItems());
        Assert.Single(inbox.GetSnoozedItems());

        // Restore due snoozes
        inbox.RestoreDueSnoozes(snoozeUntil.AddMinutes(5));
        Assert.Single(inbox.GetPendingItems());
        Assert.Equal(InboxState.Unheard, inbox.GetPendingItems()[0].State);

        // Accept
        var likedTrackId = "";
        inbox.Accept(item.Id, id => likedTrackId = id);
        Assert.Equal(snap.Id.ToString(), likedTrackId);
        Assert.Empty(inbox.GetPendingItems());
        Assert.Single(inbox.GetDecidedItems());
        Assert.Equal(InboxState.Accepted, inbox.GetDecidedItems()[0].State);
    }

    [Fact]
    public void AutomationService_ConditionMatchingAndCooldown_ExecutesActions()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var engine = new FakePlayerEngine();
        var queue = new QueueService();
        var playback = new PlaybackService(engine, queue);

        var currentContext = new ListeningContext(
            Hour: 14,
            DayOfWeek: 6,
            IsWeekend: true,
            FrontmostAppBundleId: "com.microsoft.VSCode",
            OutputDeviceName: "Sony WH-1000XM4",
            IsHeadphones: true);

        var executedActions = new List<(AutomationAction Action, string Title)>();

        using var automation = new AutomationService(
            store,
            playback,
            contextProvider: () => currentContext,
            actionHandler: (act, tr) => executedActions.Add((act, tr.Title)));

        // Add Rule: When track completes on weekend with headphones -> LikeTrack, with 5000ms cooldown
        var cond = new AutomationConditions(IsHeadphones: true, IsWeekend: true, TimeBand: TimeBand.Afternoon);
        var rule = automation.AddRule("Weekend Headphones Like", AutomationTrigger.TrackCompleted, cond, AutomationAction.LikeTrack, cooldownMs: 5000);

        var snap = TrackSnapshot.Test("Chill Track", "yt-auto-1");

        // Fire TrackCompleted
        playback.EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackCompleted, snap));
        Assert.Single(executedActions);
        Assert.Equal(AutomationAction.LikeTrack, executedActions[0].Action);

        // Immediate second fire -> blocked by cooldown
        playback.EventBus.Post(new PlaybackEvent(PlaybackEventKind.TrackCompleted, snap));
        Assert.Single(executedActions); // Still 1

        // Disable rule -> fire does nothing
        automation.SetEnabled(rule.Id, false);
        Assert.False(automation.GetRules().First(r => r.Id == rule.Id).Enabled);
    }

    [Fact]
    public void FocusService_CountdownAndFormatting_OperatesCorrectly()
    {
        using var store = SqliteStore.Open(inMemory: true);
        var engine = new FakePlayerEngine();
        var queue = new QueueService();
        var playback = new PlaybackService(engine, queue);
        using var focus = new FocusService(store, playback);

        Assert.False(focus.IsActive);
        focus.Start(minutes: 45, queueLocked: true, expiration: FocusExpiration.Pause, pomodoro: false);

        Assert.True(focus.IsActive);
        Assert.True(focus.IsQueueLocked);
        Assert.Equal(45 * 60, focus.TotalSeconds);
        Assert.Equal("45:00", focus.RemainingFormatted);

        focus.Stop(completedByTimer: false);
        Assert.False(focus.IsActive);
        Assert.False(focus.IsQueueLocked);
        Assert.Single(store.GetSessions());
        Assert.Equal(FocusStatus.Cancelled, store.GetSessions()[0].Status);
    }
}

namespace Muses.Core.Advanced;

public interface IEQRepository
{
    void SavePreset(EQPresetEntity preset);
    IReadOnlyList<EQPresetEntity> GetPresets();
    void DeletePreset(string id);
}

public interface INotesRepository
{
    TrackNoteEntity? GetNote(string trackId);
    void SetNote(string trackId, string content);
    void DeleteNote(string trackId);
    IReadOnlyList<TrackBookmarkEntity> GetBookmarks(string trackId);
    void AddBookmark(TrackBookmarkEntity bookmark);
    void UpdateBookmark(string id, string? title, string? note);
    void DeleteBookmark(string id);
    IReadOnlyList<NoteSearchHit> SearchNotes(string query, IReadOnlyDictionary<string, string> trackTitles);
}

public interface IInboxRepository
{
    IReadOnlyList<InboxItemEntity> GetItems();
    void SaveItem(InboxItemEntity item);
    void DeleteItem(string id);
    void UpdateState(string id, InboxState state, long? snoozeUntil = null);
    void UpdateNotes(string id, string? notes);
    void RestoreDueSnoozes(long nowUnixMs);
}

public interface IAutomationRepository
{
    IReadOnlyList<AutomationRuleEntity> GetRules();
    void SaveRule(AutomationRuleEntity rule);
    void DeleteRule(string id);
    void SetEnabled(string id, bool enabled);
    void RecordFire(string id, long firedAt);
}

public interface IFocusRepository
{
    void SaveSession(FocusSessionEntity session);
    void UpdateSession(FocusSessionEntity session);
    IReadOnlyList<FocusSessionEntity> GetSessions();
}

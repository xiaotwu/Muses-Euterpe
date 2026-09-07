namespace Muses.Core.History;

public interface IHistoryRepository
{
    void RecordEvent(ListeningEventEntity evt);
    IReadOnlyList<ListeningEventEntity> GetEvents(long? fromStartedAt = null, long? toStartedAt = null);
    void ClearHistory();
}

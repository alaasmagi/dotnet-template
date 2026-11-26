namespace Contracts.DataAccess;

/// <summary>
/// Stages and queries the ids of consumed messages used for idempotent message handling.
/// </summary>
public interface IProcessedMessageRepository
{
    Task<bool> ExistsAsync(Guid messageId);

    void Add(Guid messageId);

    Task<IReadOnlyList<Guid>> GetIdsProcessedBeforeAsync(DateTimeOffset cutoff, int count);

    void Remove(IEnumerable<Guid> messageIds);
}

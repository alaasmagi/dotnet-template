using Contracts.DataAccess;
using DataAccess.Context;
using DTO.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public class ProcessedMessageRepository : IProcessedMessageRepository
{
    private readonly AppDbContext _dbContext;

    public ProcessedMessageRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(Guid messageId)
    {
        return _dbContext.ProcessedMessages.AnyAsync(message => message.MessageId == messageId);
    }

    public void Add(Guid messageId)
    {
        _dbContext.ProcessedMessages.Add(new ProcessedMessageEntity
        {
            MessageId = messageId,
            ProcessedAt = DateTimeOffset.UtcNow
        });
    }

    public async Task<IReadOnlyList<Guid>> GetIdsProcessedBeforeAsync(DateTimeOffset cutoff, int count)
    {
        return await _dbContext.ProcessedMessages
            .Where(message => message.ProcessedAt < cutoff)
            .OrderBy(message => message.ProcessedAt)
            .Select(message => message.MessageId)
            .Take(count)
            .ToListAsync();
    }

    public void Remove(IEnumerable<Guid> messageIds)
    {
        // Key-only stubs, so the rows are deleted without loading them.
        _dbContext.ProcessedMessages.RemoveRange(messageIds.Select(id => new ProcessedMessageEntity { MessageId = id }));
    }
}

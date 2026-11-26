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

    public async Task<int> RemoveProcessedBeforeAsync(DateTimeOffset cutoff)
    {
        var expired = await _dbContext.ProcessedMessages
            .Where(message => message.ProcessedAt < cutoff)
            .ToListAsync();
        _dbContext.ProcessedMessages.RemoveRange(expired);
        return expired.Count;
    }
}

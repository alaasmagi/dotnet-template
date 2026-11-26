using Base.Contracts.DataAccess;
using Base.Contracts.DTO;
using Base.DTO;
using Contracts.Application;
using Contracts.DataAccess;

namespace Application;

public class ProcessedMessageService(
    IProcessedMessageRepository processedMessageRepository,
    IBaseUow uow) : IProcessedMessageService
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    public const int BatchSize = 1000;

    public async Task<IMethodResponse<int>> RemoveExpiredAsync()
    {
        var cutoff = DateTimeOffset.UtcNow - Retention;
        var removed = 0;

        while (true)
        {
            var batch = await processedMessageRepository.GetIdsProcessedBeforeAsync(cutoff, BatchSize);
            if (batch.Count == 0)
            {
                return MethodResponse<int>.Success(removed);
            }

            processedMessageRepository.Remove(batch);
            var saved = await uow.TrySaveChangesAsync();
            if (!saved.Successful)
            {
                return saved;
            }

            removed += batch.Count;
            if (batch.Count < BatchSize)
            {
                return MethodResponse<int>.Success(removed);
            }
        }
    }
}

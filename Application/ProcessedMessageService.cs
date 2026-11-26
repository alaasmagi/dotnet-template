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

    public async Task<IMethodResponse<int>> RemoveExpiredAsync()
    {
        var removed = await processedMessageRepository.RemoveProcessedBeforeAsync(DateTimeOffset.UtcNow - Retention);
        if (removed == 0)
        {
            return MethodResponse<int>.Success(0);
        }

        var saved = await uow.TrySaveChangesAsync();
        return saved.Successful ? MethodResponse<int>.Success(removed) : saved;
    }
}

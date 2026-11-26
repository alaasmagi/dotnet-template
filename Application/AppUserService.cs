using Base.Application;
using Base.Contracts.DataAccess;
using Base.Contracts.DTO;
using Base.DTO;
using Contracts.Application;
using Contracts.DataAccess;
using Domain;

namespace Application;

public class AppUserService(
    IAppUserRepository appUserRepository,
    IProcessedMessageRepository processedMessageRepository,
    IBaseUow uow,
    IMapper<AppUser, AppUser> mapper)
    : BaseService<AppUser, AppUser, IAppUserRepository>(uow, appUserRepository, mapper), IAppUserService
{
    public async Task<IMethodResponse<bool>> SyncFromIdentityAsync(Guid messageId, AppUser appUser)
    {
        if (await processedMessageRepository.ExistsAsync(messageId))
        {
            return MethodResponse<bool>.Success(false);
        }

        var existing = await ServiceRepository.GetByIdAsync(appUser.Id);

        IMethodResponse<AppUser> staged;
        if (existing.Successful)
        {
            staged = await ServiceRepository.UpdateAsync(appUser.Id, appUser, existing.Value!.ConcurrencyToken);
        }
        else if (existing.Error?.Code == ErrorDefaults.Codes.NotFound)
        {
            staged = await ServiceRepository.CreateAsync(appUser);
        }
        else
        {
            return MethodResponse<bool>.Failure(existing.Error!);
        }

        if (!staged.Successful)
        {
            return MethodResponse<bool>.Failure(staged.Error!);
        }

        return await SaveWithMessageIdAsync(messageId);
    }

    public async Task<IMethodResponse<bool>> RemoveFromIdentityAsync(Guid messageId, Guid id)
    {
        if (await processedMessageRepository.ExistsAsync(messageId))
        {
            return MethodResponse<bool>.Success(false);
        }

        var existing = await ServiceRepository.GetByIdAsync(id);
        if (existing.Successful)
        {
            var removed = await ServiceRepository.RemoveAsync(id, existing.Value!.ConcurrencyToken);
            if (!removed.Successful)
            {
                return removed;
            }
        }
        // An already missing user is the desired end state of a delete event.
        else if (existing.Error?.Code != ErrorDefaults.Codes.NotFound)
        {
            return MethodResponse<bool>.Failure(existing.Error!);
        }

        return await SaveWithMessageIdAsync(messageId);
    }

    private async Task<IMethodResponse<bool>> SaveWithMessageIdAsync(Guid messageId)
    {
        // Saved in the same transaction as the staged AppUser change.
        processedMessageRepository.Add(messageId);

        var saved = await ServiceUow.TrySaveChangesAsync();
        if (saved.Successful)
        {
            return MethodResponse<bool>.Success(true);
        }

        // A concurrent delivery of the same message wins the primary key, which makes this one a duplicate.
        return await processedMessageRepository.ExistsAsync(messageId)
            ? MethodResponse<bool>.Success(false)
            : MethodResponse<bool>.Failure(saved.Error!);
    }
}

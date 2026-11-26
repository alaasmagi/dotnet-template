using Base.Contracts.Application;
using Base.Contracts.DTO;
using Domain;

namespace Contracts.Application;

/// <summary>
/// AppUser projection of the Keycloak user. The identity methods return false when the message id was already processed.
/// </summary>
public interface IAppUserService : IBaseService<AppUser>
{
    Task<IMethodResponse<bool>> SyncFromIdentityAsync(Guid messageId, AppUser appUser);

    Task<IMethodResponse<bool>> RemoveFromIdentityAsync(Guid messageId, Guid id);
}

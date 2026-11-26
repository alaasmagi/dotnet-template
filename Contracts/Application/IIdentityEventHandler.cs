using Domain;

namespace Contracts.Application;

/// <summary>
/// Decides how an identity-hub user event is applied to the AppUser projection. appUser is null when the content could not be read.
/// </summary>
public interface IIdentityEventHandler
{
    Task<MessageHandlingResult> HandleAsync(string messageId, string action, string contentVersion, AppUser? appUser);
}

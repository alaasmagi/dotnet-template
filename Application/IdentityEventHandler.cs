using Base.DTO;
using Contracts.Application;
using Domain;

namespace Application;

public class IdentityEventHandler(IAppUserService appUserService) : IIdentityEventHandler
{
    public const string SupportedContentVersion = "1.0";

    public async Task<MessageHandlingResult> HandleAsync(string messageId, string action, string contentVersion, AppUser? appUser)
    {
        var isUserSync = action is "user-created" or "user-updated" or "user-enabled" or "user-disabled";
        if (!isUserSync && action != "user-deleted")
        {
            return MessageHandlingResult.Ignored;
        }

        if (contentVersion != SupportedContentVersion || appUser == null || !Guid.TryParse(messageId, out var id))
        {
            return MessageHandlingResult.Reject;
        }

        var result = isUserSync
            ? await appUserService.SyncFromIdentityAsync(id, appUser)
            : await appUserService.RemoveFromIdentityAsync(id, appUser.Id);

        if (!result.Successful)
        {
            // Another writer changed the user between read and save; a redelivery reads the new concurrency token.
            return result.Error?.Code == ErrorDefaults.Codes.ConcurrencyConflict
                ? MessageHandlingResult.Retry
                : MessageHandlingResult.Reject;
        }

        return result.Value ? MessageHandlingResult.Applied : MessageHandlingResult.AlreadyProcessed;
    }
}

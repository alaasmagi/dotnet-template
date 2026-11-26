using System.Data.Common;
using System.Text.Json;
using Base.Contracts.DTO;
using Base.Contracts.Message;
using Base.Keycloak.Events;
using Base.Message;
using Contracts.Application;
using Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace External.RabbitMQ;

public class RabbitMqEventHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqEventHandler> logger)
{
    private const string IdentityContentVersion = "1.0";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<MessageDisposition> HandleAsync(IBaseEventEnvelope<JsonElement> @event, CancellationToken ct)
    {
        if (string.Equals(@event.Source, DefaultSources.IdentityHub, StringComparison.Ordinal))
        {
            try
            {
                return await HandleIdentityEventAsync(@event);
            }
            catch (Exception exception) when (IsTransientDatabaseFailure(exception))
            {
                logger.LogWarning(exception, "Transient database failure for message {MessageId}, requeueing.", @event.Id);
                return MessageDisposition.Requeue;
            }
        }

        logger.LogInformation(
            "Received event {EventAction} from {EventSource}. Add a handler branch for this event.",
            @event.Action,
            @event.Source);
        return MessageDisposition.Ack;
    }

    private async Task<MessageDisposition> HandleIdentityEventAsync(IBaseEventEnvelope<JsonElement> @event)
    {
        var isUserSync = @event.Action is "user-created" or "user-updated" or "user-enabled" or "user-disabled";
        if (!isUserSync && @event.Action != "user-deleted")
        {
            logger.LogDebug("Ignoring unsupported identity event action {EventAction}.", @event.Action);
            return MessageDisposition.Ack;
        }

        if (@event.ContentVersion != IdentityContentVersion)
        {
            logger.LogError(
                "Rejecting identity event {MessageId} with unsupported contentVersion {ContentVersion}.",
                @event.Id,
                @event.ContentVersion);
            return MessageDisposition.Reject;
        }

        var content = ReadUserEventContent(@event.Content);
        if (!Guid.TryParse(@event.Id, out var messageId) || content == null || !Guid.TryParse(content.UserId, out var userId))
        {
            logger.LogError("Rejecting identity event {MessageId} with invalid id or content.", @event.Id);
            return MessageDisposition.Reject;
        }

        using var scope = scopeFactory.CreateScope();
        var appUserService = scope.ServiceProvider.GetRequiredService<IAppUserService>();
        IMethodResponse<bool> result = isUserSync
            ? await appUserService.SyncFromIdentityAsync(
                messageId,
                AppUser.FromIdentity(userId, content.Email, content.Username, content.FullName, content.Locale, content.TimeZone))
            : await appUserService.RemoveFromIdentityAsync(messageId, userId);

        if (!result.Successful)
        {
            logger.LogError(
                "Rejecting identity event {MessageId} ({EventAction}) for Keycloak user {UserId}: {ErrorCode} {ErrorMessage}",
                messageId,
                @event.Action,
                userId,
                result.Error?.Code,
                result.Error?.Message);
            return MessageDisposition.Reject;
        }

        if (result.Value)
        {
            logger.LogInformation("Applied identity event {EventAction} for Keycloak user {UserId}.", @event.Action, userId);
        }
        else
        {
            logger.LogInformation("Skipped already processed identity event {MessageId}.", messageId);
        }

        return MessageDisposition.Ack;
    }

    private static UserEventContent? ReadUserEventContent(JsonElement content)
    {
        try
        {
            return content.Deserialize<UserEventContent>(SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsTransientDatabaseFailure(Exception? exception)
    {
        for (; exception != null; exception = exception.InnerException)
        {
            if (exception is DbException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }
}

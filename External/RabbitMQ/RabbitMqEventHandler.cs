using System.Data.Common;
using System.Text.Json;
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
                using var scope = scopeFactory.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<IIdentityEventHandler>().HandleAsync(
                    @event.Id,
                    @event.Action,
                    @event.ContentVersion,
                    ReadAppUser(@event.Content));
                return ToDisposition(@event, result);
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

    private MessageDisposition ToDisposition(IBaseEventEnvelope<JsonElement> @event, MessageHandlingResult result)
    {
        switch (result)
        {
            case MessageHandlingResult.Applied:
                logger.LogInformation("Applied identity event {MessageId} ({EventAction}).", @event.Id, @event.Action);
                return MessageDisposition.Ack;
            case MessageHandlingResult.AlreadyProcessed:
                logger.LogInformation("Skipped already processed identity event {MessageId}.", @event.Id);
                return MessageDisposition.Ack;
            case MessageHandlingResult.Ignored:
                logger.LogDebug("Ignoring unsupported identity event action {EventAction}.", @event.Action);
                return MessageDisposition.Ack;
            case MessageHandlingResult.Retry:
                logger.LogWarning("Requeueing identity event {MessageId} ({EventAction}) after a concurrency conflict.", @event.Id, @event.Action);
                return MessageDisposition.Requeue;
            default:
                logger.LogError(
                    "Rejecting identity event {MessageId} ({EventAction}, contentVersion {ContentVersion}).",
                    @event.Id,
                    @event.Action,
                    @event.ContentVersion);
                return MessageDisposition.Reject;
        }
    }

    private static AppUser? ReadAppUser(JsonElement content)
    {
        UserEventContent? parsed;
        try
        {
            parsed = content.Deserialize<UserEventContent>(SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        return parsed != null && Guid.TryParse(parsed.UserId, out var userId)
            ? AppUser.FromIdentity(userId, parsed.Email, parsed.Username, parsed.FullName, parsed.Locale, parsed.TimeZone)
            : null;
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

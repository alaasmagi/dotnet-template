using System.Text.Json;
using Base.Contracts.Message;
using Base.Message;
using Base.Message.RabbitMQ;
using Microsoft.Extensions.Logging;

namespace External.RabbitMQ;

public class RabbitMqEventConsumer : RabbitMqDeliveryConsumerBase<JsonElement>
{
    private readonly RabbitMqEventHandler _handler;

    public RabbitMqEventConsumer(
        RabbitMqConnectionManager connections,
        AppMessagingOptions appOptions,
        RabbitMqEventHandler handler,
        ILogger<RabbitMqEventConsumer> logger)
        : base(
            connections,
            logger,
            queueName: appOptions.Queue)
    {
        _handler = handler;
    }

    protected override Task<MessageDisposition> HandleAsync(
        BaseEventEnvelope<JsonElement> envelope,
        CancellationToken cancellationToken)
    {
        return _handler.HandleAsync(envelope, cancellationToken);
    }
}

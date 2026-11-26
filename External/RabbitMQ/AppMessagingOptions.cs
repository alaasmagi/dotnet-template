namespace External.RabbitMQ;

public class AppMessagingOptions
{
    public string Queue { get; init; } = default!;
    public string AppSlug { get; init; } = default!;
}

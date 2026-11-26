namespace Domain;

/// <summary>
/// Provider-neutral outcome of handling a consumed message; the messaging layer maps it to ack, requeue or reject.
/// </summary>
public enum MessageHandlingResult
{
    Applied,
    AlreadyProcessed,
    Ignored,
    Retry,
    Reject
}

using Base.Contracts.DTO;

namespace Contracts.Application;

/// <summary>
/// Maintains the processed message ids used for idempotent message handling.
/// </summary>
public interface IProcessedMessageService
{
    Task<IMethodResponse<int>> RemoveExpiredAsync();
}

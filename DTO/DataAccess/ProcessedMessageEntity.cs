using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DTO.DataAccess;

/// <summary>
/// Id of a consumed message, kept for idempotency as defined in the platform event contract.
/// </summary>
public class ProcessedMessageEntity
{
    [Key]
    [Column("message_id")]
    public Guid MessageId { get; set; }

    [Column("processed_at")]
    public DateTimeOffset ProcessedAt { get; set; }

    [Column("status")]
    public string? Status { get; set; }
}

namespace PolicyManager.Models;

/// <summary>
/// Represents a message stored in the transactional outbox before being published to a message broker.
/// </summary>
public class OutboxMessage
{
    /// <summary>
    /// Unique identifier for the outbox message.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The type of the message or event.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Serialized JSON payload of the message.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the message was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the message was processed and published. Null if unprocessed.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? Error { get; set; }
}

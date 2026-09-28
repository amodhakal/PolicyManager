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

    /// <summary>
    /// How many times delivery has been attempted, including the attempt in progress once it fails.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Earliest time the next delivery attempt may run. Null means "eligible now".
    /// </summary>
    /// <remarks>
    /// Set from the backoff schedule after a failed attempt, so a broker outage does not turn into a
    /// hot retry loop against a dependency that is known to be down.
    /// </remarks>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// Identifies the processor instance that currently holds this message.
    /// </summary>
    /// <remarks>
    /// Claiming is what makes horizontal scaling safe. Two instances polling the same table would
    /// otherwise both read the same pending rows and publish them twice.
    /// </remarks>
    public Guid? LockToken { get; set; }

    /// <summary>
    /// When the current claim expires. Null when the message is not claimed.
    /// </summary>
    /// <remarks>
    /// A lease rather than a permanent lock, so a processor that dies mid-batch releases its work
    /// instead of stranding the message until someone intervenes.
    /// </remarks>
    public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// When the message was abandoned after exhausting its delivery attempts. Null while it is still live.
    /// </summary>
    /// <remarks>
    /// Dead-lettering is deliberately distinct from <see cref="ProcessedAt" />: a dead-lettered message
    /// was never delivered, and conflating the two would make the table look drained when it is not.
    /// </remarks>
    public DateTime? DeadLetteredAt { get; set; }
}

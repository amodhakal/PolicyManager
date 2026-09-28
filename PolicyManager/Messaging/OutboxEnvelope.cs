using PolicyManager.Models;

namespace PolicyManager.Messaging;

/// <summary>
///     The contract the transactional outbox publishes to the broker.
/// </summary>
/// <remarks>
///     <para>
///     An envelope rather than the domain entity, for two reasons. The outbox table is polymorphic —
///     a single <c>Type</c> discriminator over a JSON <c>Content</c> blob — and a broker needs a
///     concrete contract per message type to route, size and version. Publishing the raw row would
///     push that ambiguity onto every consumer.
///     </para>
///     <para>
///     The payload stays a string on purpose. Re-serialising <c>Content</c> into a typed shape here
///     would mean a second schema, and a payload written by a version of the API that has since
///     changed would fail to deserialise in the relay. The wire contract is the envelope; the
///     domain contract inside <c>Content</c> is still versioned by the message's own <c>Type</c>.
///     </para>
/// </remarks>
public sealed record OutboxEnvelope
{
    /// <summary>
    ///     The outbox row's identifier, reused as the broker message identifier.
    /// </summary>
    /// <remarks>
    ///     Carried through rather than generated so a consumer can correlate what it receives with
    ///     what the outbox recorded, and so a redelivery is recognisable as the same message.
    /// </remarks>
    public required Guid MessageId { get; init; }

    /// <summary>
    ///     The domain event discriminator, such as <c>PolicyCreated</c>.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    ///     The serialised event payload, exactly as it was written to the outbox.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    ///     When the outbox row was created, which is when the domain change committed.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    ///     How many delivery attempts had already failed when this one was made.
    /// </summary>
    /// <remarks>
    ///     Lets a consumer tell a first delivery from a redelivery, and gives it a way to detect a
    ///     poison message that keeps coming back.
    /// </remarks>
    public int AttemptCount { get; init; }

    /// <summary>
    ///     Projects an outbox row onto the wire contract.
    /// </summary>
    /// <param name="message">The claimed outbox row about to be published.</param>
    /// <returns>The envelope to publish.</returns>
    public static OutboxEnvelope FromOutboxMessage(OutboxMessage message) => new()
    {
        MessageId = message.Id,
        Type = message.Type,
        Content = message.Content,
        CreatedAt = message.CreatedAt,
        AttemptCount = message.AttemptCount
    };
}

using PolicyManager.Models;

namespace PolicyManager.Services;

/// <summary>
///     Publishes a single outbox message to the message broker.
/// </summary>
/// <remarks>
///     An interface rather than a direct broker call so the dispatch, retry and dead-lettering policy
///     can be tested against a publisher that fails on demand, and so the transport can be replaced
///     without touching the polling logic.
/// </remarks>
public interface IOutboxPublisher
{
    /// <summary>
    ///     Publishes one message.
    /// </summary>
    /// <param name="message">The message to publish.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="Exception">
    ///     Any transport failure. The dispatcher treats a throw as a failed attempt and schedules a
    ///     retry, so implementations should let transport exceptions propagate rather than swallow them.
    /// </exception>
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

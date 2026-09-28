using Microsoft.Extensions.Logging;
using PolicyManager.Models;

namespace PolicyManager.Services;

/// <summary>
///     The default <see cref="IOutboxPublisher" />, which records the message and marks it delivered.
/// </summary>
/// <remarks>
///     This is the stand-in the transactional outbox shipped with: it drains the table correctly and
///     honours the outbox contract, but there is no broker on the other end yet. Replacing it with a
///     real transport is a matter of registering a different <see cref="IOutboxPublisher" />; nothing
///     in the dispatcher, the retry policy or the dead-lettering changes.
/// </remarks>
public class LoggingOutboxPublisher(ILogger<LoggingOutboxPublisher> logger) : IOutboxPublisher
{
    /// <summary>
    ///     Logs the message as published.
    /// </summary>
    /// <param name="message">The message being delivered.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Outbox message {MessageId} of type {MessageType} published.",
            message.Id,
            message.Type);

        return Task.CompletedTask;
    }
}

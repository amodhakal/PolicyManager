using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;
using PolicyManager.Data;
using PolicyManager.Models;

namespace PolicyManager.Services;

/// <summary>
///     Claims a batch of due outbox messages, publishes them, and records the outcome of each attempt.
/// </summary>
/// <remarks>
///     <para>
///     Holding the policy here rather than in the background service keeps the loop in
///     <see cref="OutboxProcessorBackgroundService" /> down to scheduling, and makes the interesting
///     behaviour — claiming, retry, dead-lettering — reachable from a test without starting a host.
///     </para>
///     <para>
///     Claiming is a two-step read-then-conditional-update. The read narrows the table to plausible
///     candidates; the update is a single statement that only touches rows no other processor currently
///     holds, so exactly one instance wins each row. The read alone would not be safe, and an
///     unconditional update would let two instances claim the same message.
///     </para>
/// </remarks>
public class OutboxDispatcher(
    AppDbContext context,
    IOutboxPublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger)
{
    private readonly OutboxOptions _options = options.Value;

    /// <summary>
    ///     Claims and attempts one batch of due messages.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of messages this pass successfully delivered.</returns>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var claimed = await ClaimBatchAsync(now, cancellationToken);
        if (claimed.Count == 0) return 0;

        var delivered = 0;

        foreach (var message in claimed)
        {
            if (await TryPublishAsync(message, now, cancellationToken)) delivered++;
        }

        await context.SaveChangesAsync(cancellationToken);
        return delivered;
    }

    /// <summary>
    ///     Atomically reserves up to one batch of due messages for this processor instance.
    /// </summary>
    private async Task<List<OutboxMessage>> ClaimBatchAsync(DateTime now, CancellationToken cancellationToken)
    {
        var lockToken = Guid.NewGuid();
        var lockExpiry = now.Add(_options.LockDuration);

        var candidateIds = await context.OutboxMessages
            .Where(m => m.ProcessedAt == null
                        && m.DeadLetteredAt == null
                        && (m.NextAttemptAt == null || m.NextAttemptAt <= now)
                        && (m.LockedUntil == null || m.LockedUntil <= now))
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0) return [];

        // The guard is repeated in the UPDATE on purpose. The read above is only a narrowing pass;
        // between it and here another instance may have claimed some of these rows, and the second
        // instance to run this statement must get zero rows for anything the first one took.
        await context.OutboxMessages
            .Where(m => candidateIds.Contains(m.Id)
                        && m.ProcessedAt == null
                        && m.DeadLetteredAt == null
                        && (m.LockedUntil == null || m.LockedUntil <= now))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.LockToken, lockToken)
                    .SetProperty(m => m.LockedUntil, lockExpiry),
                cancellationToken);

        return await context.OutboxMessages
            .Where(m => m.LockToken == lockToken)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    ///     Publishes one claimed message and records success, a scheduled retry, or a dead letter.
    /// </summary>
    /// <returns>True when the message was delivered.</returns>
    private async Task<bool> TryPublishAsync(OutboxMessage message, DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            await publisher.PublishAsync(message, cancellationToken);

            message.ProcessedAt = now;
            message.Error = null;
            message.NextAttemptAt = null;
            ReleaseClaim(message);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            message.AttemptCount++;
            message.Error = ex.Message;
            ReleaseClaim(message);

            if (message.AttemptCount >= _options.MaxAttempts)
            {
                message.DeadLetteredAt = now;
                message.NextAttemptAt = null;
                logger.LogError(
                    ex,
                    "Outbox message {MessageId} of type {MessageType} dead-lettered after {AttemptCount} attempts.",
                    message.Id,
                    message.Type,
                    message.AttemptCount);
            }
            else
            {
                var delay = _options.GetRetryDelay(message.AttemptCount);
                message.NextAttemptAt = now.Add(delay);
                logger.LogWarning(
                    ex,
                    "Outbox message {MessageId} of type {MessageType} failed on attempt {AttemptCount}; retrying in {RetryDelay}.",
                    message.Id,
                    message.Type,
                    message.AttemptCount,
                    delay);
            }

            return false;
        }
    }

    private static void ReleaseClaim(OutboxMessage message)
    {
        message.LockToken = null;
        message.LockedUntil = null;
    }
}

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
///     Every write here goes through <c>ExecuteUpdateAsync</c> and every read through
///     <c>AsNoTracking</c>, deliberately. <c>ExecuteUpdateAsync</c> bypasses the change tracker, so a
///     tracked query issued after it returns the <em>stale</em> already-tracked instance rather than
///     the row that was just written. Mutating that instance silently updates nothing: nulling a value
///     the tracker already believes is null produces no UPDATE, so a released claim would stay held and
///     the message would never be retried. Coordinating entirely through the database removes that
///     class of bug rather than working around it.
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
            var outcome = await PublishAsync(message, now, cancellationToken);
            await RecordOutcomeAsync(message, outcome, now, cancellationToken);
            if (outcome.WasDelivered) delivered++;
        }

        return delivered;
    }

    /// <summary>
    ///     Atomically reserves up to one batch of due messages for this processor instance.
    /// </summary>
    /// <returns>The messages this instance now holds, read fresh from the database.</returns>
    private async Task<List<OutboxMessage>> ClaimBatchAsync(DateTime now, CancellationToken cancellationToken)
    {
        var lockToken = Guid.NewGuid();
        var lockExpiry = now.Add(_options.LockDuration);

        var candidateIds = await context.OutboxMessages
            .AsNoTracking()
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
            .AsNoTracking()
            .Where(m => m.LockToken == lockToken)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    ///     Publishes one claimed message and decides what should happen to it.
    /// </summary>
    /// <param name="message">The claimed message.</param>
    /// <param name="now">The current time, from the injected clock, used to schedule the retry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The outcome to persist. Nothing is written here.</returns>
    private async Task<Outcome> PublishAsync(
        OutboxMessage message,
        DateTime now,
        CancellationToken cancellationToken)
    {
        try
        {
            await publisher.PublishAsync(message, cancellationToken);
            return Outcome.Succeeded(message.AttemptCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var attempts = message.AttemptCount + 1;

            if (attempts >= _options.MaxAttempts)
            {
                logger.LogError(
                    ex,
                    "Outbox message {MessageId} of type {MessageType} dead-lettered after {AttemptCount} attempts.",
                    message.Id,
                    message.Type,
                    attempts);

                return Outcome.DeadLetter(attempts, ex.Message);
            }

            var delay = _options.GetRetryDelay(attempts);

            logger.LogWarning(
                ex,
                "Outbox message {MessageId} of type {MessageType} failed on attempt {AttemptCount}; retrying in {RetryDelay}.",
                message.Id,
                message.Type,
                attempts,
                delay);

            return Outcome.Retry(attempts, ex.Message, now.Add(delay));
        }
    }

    /// <summary>
    ///     Writes the outcome back and releases the claim, in one statement.
    /// </summary>
    private Task RecordOutcomeAsync(
        OutboxMessage message,
        Outcome outcome,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var processedAt = outcome.WasDelivered ? now : (DateTime?)null;
        var deadLetteredAt = outcome.IsDeadLettered ? now : (DateTime?)null;
        var releasedToken = (Guid?)null;
        var releasedLock = (DateTime?)null;
        var error = outcome.Error;
        var nextAttemptAt = outcome.NextAttemptAt;
        var attemptCount = outcome.AttemptCount;

        // Guarded on still holding the claim. If the lease expired and another instance took the
        // message while this one was publishing, the outcome is dropped rather than overwriting a
        // claim this instance no longer owns.
        return context.OutboxMessages
            .Where(m => m.Id == message.Id && m.LockToken == message.LockToken)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.ProcessedAt, processedAt)
                    .SetProperty(m => m.Error, error)
                    .SetProperty(m => m.AttemptCount, attemptCount)
                    .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                    .SetProperty(m => m.DeadLetteredAt, deadLetteredAt)
                    .SetProperty(m => m.LockToken, releasedToken)
                    .SetProperty(m => m.LockedUntil, releasedLock),
                cancellationToken);
    }

    /// <summary>
    ///     What should become of a message after one delivery attempt.
    /// </summary>
    /// <param name="WasDelivered">Whether the message reached the broker.</param>
    /// <param name="AttemptCount">Total attempts made, including this one.</param>
    /// <param name="Error">The failure message, or null on success.</param>
    /// <param name="NextAttemptAt">When to retry, or null if it should not be retried.</param>
    /// <param name="IsDeadLettered">Whether the attempt budget is exhausted.</param>
    private sealed record Outcome(
        bool WasDelivered,
        int AttemptCount,
        string? Error,
        DateTime? NextAttemptAt,
        bool IsDeadLettered)
    {
        public static Outcome Succeeded(int attempts) => new(true, attempts, null, null, false);

        public static Outcome Retry(int attempts, string error, DateTime nextAttemptAt)
            => new(false, attempts, error, nextAttemptAt, false);

        public static Outcome DeadLetter(int attempts, string error)
            => new(false, attempts, error, null, true);
    }
}

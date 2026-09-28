namespace PolicyManager.Configuration;

/// <summary>
///     Tuning for the transactional outbox poller and its delivery policy.
/// </summary>
/// <remarks>
///     Bound from the <c>Outbox</c> configuration section, so every value can be overridden per
///     environment without a rebuild.
/// </remarks>
public class OutboxOptions
{
    /// <summary>
    ///     The configuration section these options bind to.
    /// </summary>
    public const string SectionName = "Outbox";

    /// <summary>
    ///     How many messages a single pass claims and attempts.
    /// </summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    ///     How long a claimed message stays reserved for the claiming processor.
    /// </summary>
    /// <remarks>
    ///     Must comfortably exceed the time to publish one batch, or a slow batch will have its lease
    ///     expire and be picked up by a second instance while the first is still working on it.
    /// </remarks>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Delay between passes after a pass that processed at least one message.
    /// </summary>
    public TimeSpan BusyDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     Delay between passes after the first idle pass.
    /// </summary>
    public TimeSpan MinIdleDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Ceiling the idle delay backs off to.
    /// </summary>
    public TimeSpan MaxIdleDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     First retry backoff. Each subsequent failure doubles it up to <see cref="MaxRetryDelay" />.
    /// </summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Ceiling for the exponential retry backoff.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     Failed attempts tolerated before a message is dead-lettered.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    ///     Computes the delay before the next attempt of a message that has already failed
    ///     <paramref name="attemptCount" /> times.
    /// </summary>
    /// <param name="attemptCount">The number of attempts made so far, starting at 1.</param>
    /// <returns>A delay of <c>BaseRetryDelay * 2^(attemptCount - 1)</c>, capped at <see cref="MaxRetryDelay" />.</returns>
    public TimeSpan GetRetryDelay(int attemptCount)
    {
        if (attemptCount < 1) attemptCount = 1;

        // Cap the shift before it is applied: a large attempt count would otherwise overflow the
        // doubling and wrap to a negative TimeSpan, which is a shorter delay than intended.
        var shift = Math.Min(attemptCount - 1, 30);

        var delay = BaseRetryDelay * Math.Pow(2, shift);
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;
using PolicyManager.Data;
using PolicyManager.Models;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     A <see cref="TimeProvider" /> whose clock the test moves by hand.
/// </summary>
/// <remarks>
///     Retry backoff and lock expiry are both time-dependent. Advancing a fake clock keeps those tests
///     deterministic and instant, where waiting on a real clock would make the suite slow and flaky.
/// </remarks>
public sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    /// <summary>
    ///     Gets the current fake time.
    /// </summary>
    public DateTimeOffset Now => _now;

    /// <summary>
    ///     Moves the clock forward.
    /// </summary>
    /// <param name="delta">How far to advance.</param>
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>
///     A publisher that fails every message, stands in for an unreachable broker.
/// </summary>
public sealed class FailingOutboxPublisher(Exception? failure = null) : IOutboxPublisher
{
    /// <summary>
    ///     Gets the messages this publisher was asked to publish.
    /// </summary>
    public List<OutboxMessage> Attempted { get; } = [];

    /// <inheritdoc />
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        Attempted.Add(message);
        throw failure ?? new InvalidOperationException("broker unreachable");
    }
}

/// <summary>
///     Tests for outbox delivery: claiming, retry backoff, dead-lettering and the poll predicate.
/// </summary>
/// <remarks>
///     These run against real SQL Server. Claiming relies on a conditional <c>UPDATE</c>, which the
///     in-memory provider does not support at all, so there is nothing meaningful to assert there.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public class OutboxDispatcherTests : SqlServerTestBase
{
    private readonly TestTimeProvider _clock = new(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxDispatcherTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared container fixture.</param>
    public OutboxDispatcherTests(SqlServerFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    ///     A successful pass marks every message processed and stamps <c>ProcessedAt</c>.
    /// </summary>
    [Fact]
    public async Task Successful_dispatch_marks_messages_processed()
    {
        await using var context = CreateContext();
        SeedMessage(context, "PolicyCreated");

        var dispatcher = CreateDispatcher(context, new RecordingOutboxPublisher(), new OutboxOptions());

        var delivered = await dispatcher.DispatchBatchAsync();

        Assert.Equal(1, delivered);

        context.ChangeTracker.Clear();
        var stored = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.NotNull(stored.ProcessedAt);
        Assert.Null(stored.Error);
        Assert.Null(stored.LockToken);
        Assert.Null(stored.LockedUntil);
    }

    /// <summary>
    ///     A delivered message is not picked up again on the next pass.
    /// </summary>
    [Fact]
    public async Task Delivered_messages_are_not_reprocessed()
    {
        await using var context = CreateContext();
        SeedMessage(context, "PolicyCreated");

        var publisher = new RecordingOutboxPublisher();
        var dispatcher = CreateDispatcher(context, publisher, new OutboxOptions());

        Assert.Equal(1, await dispatcher.DispatchBatchAsync());
        Assert.Equal(0, await dispatcher.DispatchBatchAsync());
        Assert.Single(publisher.Published);
    }

    /// <summary>
    ///     A failed attempt records the error, increments the counter, and schedules a retry.
    /// </summary>
    [Fact]
    public async Task Failed_attempt_schedules_a_retry_with_backoff()
    {
        await using var context = CreateContext();
        SeedMessage(context, "PolicyCreated");

        var options = new OutboxOptions { BaseRetryDelay = TimeSpan.FromSeconds(5), MaxAttempts = 5 };
        var dispatcher = CreateDispatcher(context, new FailingOutboxPublisher(), options);

        Assert.Equal(0, await dispatcher.DispatchBatchAsync());

        context.ChangeTracker.Clear();
        var stored = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal("broker unreachable", stored.Error);
        Assert.Null(stored.ProcessedAt);
        Assert.Null(stored.DeadLetteredAt);
        Assert.Null(stored.LockToken);
        Assert.Equal(_clock.Now.UtcDateTime.AddSeconds(5), stored.NextAttemptAt);
    }

    /// <summary>
    ///     A retry is not attempted before its scheduled time.
    /// </summary>
    [Fact]
    public async Task Retry_is_deferred_until_the_backoff_elapses()
    {
        await using var context = CreateContext();
        SeedMessage(context, "PolicyCreated");

        var options = new OutboxOptions { BaseRetryDelay = TimeSpan.FromSeconds(30), MaxAttempts = 5 };
        var publisher = new FailingOutboxPublisher();
        var dispatcher = CreateDispatcher(context, publisher, options);

        await dispatcher.DispatchBatchAsync();
        Assert.Single(publisher.Attempted);

        // Still inside the backoff window.
        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, await dispatcher.DispatchBatchAsync());
        Assert.Single(publisher.Attempted);

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await dispatcher.DispatchBatchAsync());
        Assert.Equal(2, publisher.Attempted.Count);
    }

    /// <summary>
    ///     Each failure doubles the wait, so a sustained outage backs off instead of spinning.
    /// </summary>
    [Fact]
    public async Task Backoff_doubles_with_each_failure()
    {
        await using var context = CreateContext();
        SeedMessage(context, "PolicyCreated");

        var options = new OutboxOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(5), MaxAttempts = 10, MaxRetryDelay = TimeSpan.FromHours(1)
        };
        var publisher = new FailingOutboxPublisher();
        var dispatcher = CreateDispatcher(context, publisher, options);

        var expectedDelays = new[] { 5, 10, 20 };

        foreach (var expected in expectedDelays)
        {
            await dispatcher.DispatchBatchAsync();
            context.ChangeTracker.Clear();

            var stored = await context.OutboxMessages.AsNoTracking().SingleAsync();
            Assert.Equal(_clock.Now.UtcDateTime.AddSeconds(expected), stored.NextAttemptAt);

            _clock.Advance(TimeSpan.FromSeconds(expected));
        }
    }

    /// <summary>
    ///     A message is dead-lettered once it exhausts its attempts, and is not retried again.
    /// </summary>
    [Fact]
    public async Task Message_is_dead_lettered_after_exhausting_attempts()
    {
        await using var context = CreateContext();
        SeedMessage(context, "PolicyCreated");

        var options = new OutboxOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(1), MaxAttempts = 2, MaxRetryDelay = TimeSpan.FromMinutes(1)
        };
        var publisher = new FailingOutboxPublisher();
        var dispatcher = CreateDispatcher(context, publisher, options);

        await dispatcher.DispatchBatchAsync();
        _clock.Advance(TimeSpan.FromSeconds(2));
        await dispatcher.DispatchBatchAsync();

        context.ChangeTracker.Clear();
        var stored = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(2, stored.AttemptCount);
        Assert.NotNull(stored.DeadLetteredAt);
        Assert.Null(stored.ProcessedAt);
        Assert.Null(stored.NextAttemptAt);
        Assert.Equal("broker unreachable", stored.Error);

        // Dead-lettered rows leave the poll set entirely, so the publisher is never asked again.
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, await dispatcher.DispatchBatchAsync());
        Assert.Equal(2, publisher.Attempted.Count);
    }

    /// <summary>
    ///     A claim held by another processor is left alone, so a second instance cannot double-publish.
    /// </summary>
    [Fact]
    public async Task Message_claimed_by_another_processor_is_skipped()
    {
        await using var context = CreateContext();

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = "PolicyCreated",
            Content = "{}",
            CreatedAt = _clock.Now.UtcDateTime,
            LockToken = Guid.NewGuid(),
            LockedUntil = _clock.Now.UtcDateTime.AddSeconds(30)
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var publisher = new RecordingOutboxPublisher();
        var dispatcher = CreateDispatcher(context, publisher, new OutboxOptions());

        Assert.Equal(0, await dispatcher.DispatchBatchAsync());
        Assert.Empty(publisher.Published);
    }

    /// <summary>
    ///     An expired claim is reclaimable, so a processor that died mid-batch does not strand messages.
    /// </summary>
    [Fact]
    public async Task Expired_claim_is_reclaimable()
    {
        await using var context = CreateContext();

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = "PolicyCreated",
            Content = "{}",
            CreatedAt = _clock.Now.UtcDateTime,
            LockToken = Guid.NewGuid(),
            LockedUntil = _clock.Now.UtcDateTime.AddSeconds(30)
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var publisher = new RecordingOutboxPublisher();
        var dispatcher = CreateDispatcher(context, publisher, new OutboxOptions());

        _clock.Advance(TimeSpan.FromSeconds(31));

        Assert.Equal(1, await dispatcher.DispatchBatchAsync());
        Assert.Single(publisher.Published);
    }

    /// <summary>
    ///     Two dispatchers over the same table split the work rather than duplicating it.
    /// </summary>
    [Fact]
    public async Task Two_dispatchers_do_not_publish_the_same_message_twice()
    {
        await using var context = CreateContext();
        SeedMessage(context, "PolicyCreated");
        SeedMessage(context, "PolicyCreated");

        var publisher = new RecordingOutboxPublisher();

        // Two independent contexts, as two application instances would have.
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();

        var first = CreateDispatcher(firstContext, publisher, new OutboxOptions { BatchSize = 1 });
        var second = CreateDispatcher(secondContext, publisher, new OutboxOptions { BatchSize = 1 });

        var total = await first.DispatchBatchAsync() + await second.DispatchBatchAsync();

        Assert.Equal(2, total);
        Assert.Equal(2, publisher.Published.Count);
        Assert.Equal(2, publisher.Published.Select(m => m.Id).Distinct().Count());
    }

    /// <summary>
    ///     A pass that finds nothing to do reports zero, which is what drives the idle backoff.
    /// </summary>
    [Fact]
    public async Task Empty_outbox_reports_nothing_delivered()
    {
        await using var context = CreateContext();
        var dispatcher = CreateDispatcher(context, new RecordingOutboxPublisher(), new OutboxOptions());

        Assert.Equal(0, await dispatcher.DispatchBatchAsync());
    }

    /// <summary>
    ///     The retry schedule doubles per attempt and is capped, so a large attempt count cannot
    ///     overflow the shift into a negative delay.
    /// </summary>
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    public void Retry_delay_doubles_per_attempt(int attempt, int expectedSeconds)
    {
        var options = new OutboxOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(5), MaxRetryDelay = TimeSpan.FromSeconds(60)
        };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.GetRetryDelay(attempt));
    }

    /// <summary>
    ///     The retry delay is capped rather than growing without bound.
    /// </summary>
    [Fact]
    public void Retry_delay_is_capped()
    {
        var options = new OutboxOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(5), MaxRetryDelay = TimeSpan.FromMinutes(1)
        };

        Assert.Equal(TimeSpan.FromMinutes(1), options.GetRetryDelay(20));
        Assert.Equal(TimeSpan.FromMinutes(1), options.GetRetryDelay(int.MaxValue));
    }

    private static void SeedMessage(AppDbContext context, string type)
    {
        context.OutboxMessages.Add(new OutboxMessage { Type = type, Content = "{}", CreatedAt = DateTime.UtcNow });
        context.SaveChanges();
    }

    private OutboxDispatcher CreateDispatcher(
        AppDbContext context,
        IOutboxPublisher publisher,
        OutboxOptions options)
    {
        return new OutboxDispatcher(
            context,
            publisher,
            Options.Create(options),
            _clock,
            NullLogger<OutboxDispatcher>.Instance);
    }

    /// <summary>
    ///     A publisher that records what it was asked to publish and always succeeds.
    /// </summary>
    private sealed class RecordingOutboxPublisher : IOutboxPublisher
    {
        public List<OutboxMessage> Published { get; } = [];

        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }
}

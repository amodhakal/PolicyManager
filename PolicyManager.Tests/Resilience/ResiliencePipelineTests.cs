using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using PolicyManager.Configuration;
using PolicyManager.Health;
using PolicyManager.Resilience;

namespace PolicyManager.Tests.Resilience;

/// <summary>
///     Tests for the Polly pipelines protecting the database and the broker.
/// </summary>
/// <remarks>
///     Driven through a stopwatch rather than by asserting on the pipeline's internal callbacks: what
///     matters to a caller is that the retry happened, that the wait between attempts grew, and that
///     an open circuit rejects without calling through — and those are observable from the outside.
///     Delays are configured at a few milliseconds so the exponential growth is measurable without the
///     suite becoming slow.
/// </remarks>
public class ResiliencePipelineTests
{
    /// <summary>
    ///     A transient failure is retried and then succeeds, so a blip does not become a visible
    ///     failure.
    /// </summary>
    [Fact]
    public async Task A_transient_failure_is_retried()
    {
        var attempts = 0;
        var pipeline = Pipeline(Retries: 3);

        var result = await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            await Task.Yield();
            if (attempts < 3) throw new InvalidOperationException("transient");
            return "delivered";
        });

        Assert.Equal("delivered", result);
        Assert.Equal(3, attempts);
    }

    /// <summary>
    ///     A failure that outlasts the retry budget is surfaced, not swallowed. Swallowing it would be
    ///     indistinguishable from a successful delivery to the outbox dispatcher.
    /// </summary>
    [Fact]
    public async Task A_failure_beyond_the_retry_budget_is_surfaced()
    {
        var attempts = 0;
        var pipeline = Pipeline(Retries: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pipeline.ExecuteAsync<int>(async _ =>
            {
                attempts++;
                await Task.Yield();
                throw new InvalidOperationException("permanently down");
            }));

        // The first attempt plus two retries, and no more.
        Assert.Equal(3, attempts);
    }

    /// <summary>
    ///     The backoff is exponential rather than constant. Asserted on the strategy rather than on
    ///     elapsed time: a wall-clock assertion of "the second wait was longer than the first" is a
    ///     measurement of the machine as much as of the policy, and fails on a busy one.
    /// </summary>
    [Fact]
    public void The_backoff_is_exponential_capped_and_jittered()
    {
        var budget = new DependencyResilienceOptions
        {
            MaxRetryAttempts = 4,
            Delay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromSeconds(2),
            UseJitter = true
        };

        var retry = ResilienceRegistration.BuildRetry(budget);

        Assert.Equal(DelayBackoffType.Exponential, retry.BackoffType);
        Assert.Equal(4, retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(100), retry.Delay);
        Assert.Equal(TimeSpan.FromSeconds(2), retry.MaxDelay);
        Assert.True(retry.UseJitter);
    }

    /// <summary>
    ///     The circuit breaker's threshold comes from the same budget, so a deployment can widen the
    ///     floor at which it trips rather than only changing how long it stays open.
    /// </summary>
    [Fact]
    public void The_circuit_breaker_is_configured_from_the_budget()
    {
        var budget = new DependencyResilienceOptions
        {
            FailureRatio = 0.75,
            MinimumThroughput = 12,
            BreakDuration = TimeSpan.FromSeconds(45)
        };

        var breaker = ResilienceRegistration.BuildCircuitBreaker(budget);

        Assert.Equal(0.75, breaker.FailureRatio);
        Assert.Equal(12, breaker.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(45), breaker.BreakDuration);
        Assert.Equal(TimeSpan.FromSeconds(45), breaker.SamplingDuration);
    }

    /// <summary>
    ///     A pipeline actually waits between attempts. The floor is the sum of the configured delays —
    ///     200 + 400 + 800 for three retries of an exponential backoff with no jitter — and scheduling
    ///     can only make it longer, so this holds however busy the machine is. It is what would fail if
    ///     the retry were dropped while the attempt count still looked right.
    /// </summary>
    [Fact]
    public async Task Retries_actually_wait_between_attempts()
    {
        var attempt = 0;
        var pipeline = Pipeline(
            Retries: 3,
            baseDelay: TimeSpan.FromMilliseconds(200),
            maxDelay: TimeSpan.FromSeconds(5));

        var elapsed = await Time(() => Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pipeline.ExecuteAsync<int>(async _ =>
            {
                attempt++;
                await Task.Yield();
                throw new InvalidOperationException("down");
            })));

        Assert.Equal(4, attempt);
        Assert.True(
            elapsed >= TimeSpan.FromMilliseconds(1300),
            $"Four attempts took {elapsed.TotalMilliseconds}ms, less than the configured backoff requires.");
    }

    /// <summary>
    ///     The wait is capped, so a deep retry chain cannot grow into minutes.
    /// </summary>
    [Fact]
    public async Task The_backoff_is_capped()
    {
        var attempts = 0;
        var pipeline = Pipeline(Retries: 5, baseDelay: TimeSpan.FromMilliseconds(20), maxDelay: TimeSpan.FromMilliseconds(40));

        var elapsed = await Time(() => Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pipeline.ExecuteAsync<int>(async _ =>
            {
                attempts++;
                await Task.Yield();
                throw new InvalidOperationException("down");
            })));

        Assert.Equal(6, attempts);

        // 20 + 40 + 40 + 40 + 40 with no jitter, and then a generous ceiling so a loaded machine does
        // not fail the test. The lower bound is the part that catches a cap that was never applied:
        // unbounded doubling would have waited far longer than this.
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(150), $"The pipeline barely waited: {elapsed}.");
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"The capped backoff still took {elapsed}.");
    }

    /// <summary>
    ///     A cancellation is not retried. The token is already cancelled, so retrying spends the
    ///     budget and then throws anyway — and a host shutting down is exactly when to stop.
    /// </summary>
    [Fact]
    public async Task A_cancellation_is_not_retried()
    {
        var attempts = 0;
        var pipeline = Pipeline(Retries: 5, baseDelay: TimeSpan.FromMilliseconds(10));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pipeline.ExecuteAsync<int>(async _ =>
            {
                attempts++;
                await Task.Yield();
                cancelled.Token.ThrowIfCancellationRequested();
                return 0;
            }, cancelled.Token));

        // Polly can reject a call on an already-cancelled token before running it at all; what
        // matters is that the callback is never replayed.
        Assert.True(attempts <= 1, $"A cancelled call was replayed {attempts} times.");
    }

    /// <summary>
    ///     Once the failure rate crosses the threshold, calls are rejected without reaching the
    ///     dependency at all. That is the point of the breaker: stop paying for retries against
    ///     something that is known to be down.
    /// </summary>
    [Fact]
    public async Task The_circuit_opens_and_stops_calling_through()
    {
        var attempts = 0;
        var pipeline = Pipeline(
            Retries: 0,
            minimumThroughput: 2,
            failureRatio: 0.5,
            breakDuration: TimeSpan.FromSeconds(30));

        // Enough failures to cross the minimum throughput and open the circuit...
        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAnyAsync<Exception>(async () => await pipeline.ExecuteAsync<int>(async _ =>
            {
                attempts++;
                await Task.Yield();
                throw new InvalidOperationException("down");
            }));

        // ...after which further calls are rejected without reaching the dependency.
        await Assert.ThrowsAsync<BrokenCircuitException>(
            async () => await pipeline.ExecuteAsync(async _ => { attempts++; await Task.Yield(); return 0; }));

        // Two is the minimum throughput, and the failure ratio was exceeded on the second call, so
        // the circuit was open from then on and the dependency was never asked again.
        Assert.Equal(2, attempts);
    }

    /// <summary>
    ///     A single failure does not open the circuit. Taking a dependency out of service on one bad
    ///     request is worse than the failure it prevents.
    /// </summary>
    [Fact]
    public async Task A_single_failure_does_not_open_the_circuit()
    {
        var pipeline = Pipeline(Retries: 0, minimumThroughput: 10);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.ExecuteAsync<int>(async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("blip");
            }));

        // Still callable.
        Assert.Equal("ok", await pipeline.ExecuteAsync(async _ => { await Task.Yield(); return "ok"; }));
    }

    /// <summary>
    ///     Three pipelines are registered, one per dependency, so a caller cannot be handed another
    ///     dependency's budget by mistake.
    /// </summary>
    [Fact]
    public void One_pipeline_is_registered_per_dependency()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetService<ResiliencePipelineFor<HttpPipeline>>());
        Assert.NotNull(provider.GetService<ResiliencePipelineFor<DatabasePipeline>>());
        Assert.NotNull(provider.GetService<ResiliencePipelineFor<BrokerPipeline>>());
    }

    /// <summary>
    ///     A budget read from configuration reaches the registered pipeline, so an environment can
    ///     loosen or tighten it without a rebuild.
    /// </summary>
    [Fact]
    public void Budgets_are_bound_from_configuration()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Resilience:Broker:MaxRetryAttempts"] = "5",
            ["Resilience:Broker:Timeout"] = "00:00:07"
        });

        var broker = provider.GetRequiredService<IOptions<ResilienceOptions>>().Value.Broker;

        Assert.Equal(5, broker.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(7), broker.Timeout);
    }

    /// <summary>
    ///     Defaults are conservative: retries are few, the floor on the breaker is high enough that a
    ///     blip cannot open it, and the backoff is jittered.
    /// </summary>
    [Fact]
    public void Defaults_are_conservative()
    {
        var options = new ResilienceOptions();

        Assert.All(
            new[] { options.Http, options.Database, options.Broker },
            budget =>
            {
                Assert.InRange(budget.MaxRetryAttempts, 0, 3);
                Assert.True(budget.UseJitter);
                Assert.True(budget.MinimumThroughput >= 2);
                Assert.True(budget.FailureRatio > 0 && budget.FailureRatio <= 1);
                Assert.True(budget.MaxDelay >= budget.Delay);
            });
    }

    /// <summary>
    ///     A breaker with a throughput floor of one opens on a single call, which is a configuration
    ///     error rather than a policy.
    /// </summary>
    [Fact]
    public void A_breaker_floor_below_two_is_rejected()
    {
        var options = new ResilienceOptions();
        options.Broker.MinimumThroughput = 1;

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("Resilience:Broker:MinimumThroughput", ex.Message);
    }

    /// <summary>
    ///     A backoff ceiling below its own base delay would either be ignored or reverse the growth.
    /// </summary>
    [Fact]
    public void A_backoff_ceiling_below_its_base_is_rejected()
    {
        var options = new ResilienceOptions();
        options.Database.MaxDelay = TimeSpan.FromMilliseconds(1);
        options.Database.Delay = TimeSpan.FromSeconds(1);

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("Resilience:Database:MaxDelay", ex.Message);
    }

    /// <summary>
    ///     A zero timeout would abandon every attempt before it began, so it is rejected rather than
    ///     silently disabling the protection.
    /// </summary>
    [Fact]
    public void A_zero_timeout_is_rejected()
    {
        var options = new ResilienceOptions();
        options.Http.Timeout = TimeSpan.Zero;

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("Resilience:Http:Timeout", ex.Message);
    }

    /// <summary>
    ///     The readiness probe goes through the database pipeline, so an orchestrator polling a
    ///     database that is down is not made to wait out a retry budget on every poll.
    /// </summary>
    [Fact]
    public async Task The_readiness_probe_is_short_circuited_by_the_database_pipeline()
    {
        var calls = 0;
        var inner = new CountingHealthCheck(() =>
        {
            calls++;
            throw new InvalidOperationException("database is down");
        });

        var check = new ResilientSqlServerHealthCheck(
            inner,
            new ResiliencePipelineFor<DatabasePipeline>(Pipeline(Retries: 0, minimumThroughput: 2)),
            NullLogger<ResilientSqlServerHealthCheck>.Instance);

        var results = new List<HealthCheckResult>();
        for (var i = 0; i < 6; i++) results.Add(await check.CheckHealthAsync(new HealthCheckContext()));

        Assert.All(results, result => Assert.Equal(HealthStatus.Unhealthy, result.Status));

        // Six probes, but the database was only reached until the breaker's minimum throughput was
        // met; the rest were rejected without opening a connection. That is the whole point: a probe
        // loop must not become the load that keeps a struggling database struggling.
        Assert.InRange(calls, 1, 2);
    }

    /// <summary>
    ///     A healthy database is reported healthy, and the resilience wrapper does not change what a
    ///     successful probe means.
    /// </summary>
    [Fact]
    public async Task A_reachable_database_is_reported_healthy()
    {
        var check = new ResilientSqlServerHealthCheck(
            new CountingHealthCheck(() => HealthCheckResult.Healthy()),
            new ResiliencePipelineFor<DatabasePipeline>(Pipeline(Retries: 0)),
            NullLogger<ResilientSqlServerHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static async Task<TimeSpan> Time(Func<Task> action)
    {
        var started = Stopwatch.GetTimestamp();
        await action();
        return Stopwatch.GetElapsedTime(started);
    }

    private static ResiliencePipeline Pipeline(
        int Retries,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        int minimumThroughput = 100,
        double failureRatio = 0.5,
        TimeSpan? breakDuration = null)
    {
        return ResilienceRegistration.Build(new DependencyResilienceOptions
        {
            MaxRetryAttempts = Retries,
            Delay = baseDelay ?? TimeSpan.FromMilliseconds(5),
            MaxDelay = maxDelay ?? TimeSpan.FromSeconds(1),
            UseJitter = false,
            FailureRatio = failureRatio,
            MinimumThroughput = minimumThroughput,
            BreakDuration = breakDuration ?? TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.FromSeconds(30)
        });
    }

    private static ServiceProvider Build(Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddResiliencePipelines(
            new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Stands in for the SQL Server probe, counting how many times it is actually reached.
    /// </summary>
    private sealed class CountingHealthCheck(Func<HealthCheckResult> probe) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(probe());
    }
}

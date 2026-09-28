using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using PolicyManager.Configuration;

namespace PolicyManager.Resilience;

/// <summary>
///     Names of the HTTP clients the application configures.
/// </summary>
public static class HttpClients
{
    /// <summary>
    ///     The client for calls this application makes to other services.
    /// </summary>
    /// <remarks>
    ///     Named rather than typed, because the resilience pipeline is attached to the handler stack
    ///     and a typed client would hide that. Any outbound call should go through this name, so a new
    ///     call site is protected without anyone remembering to protect it.
    /// </remarks>
    public const string Outbound = "outbound";
}

/// <summary>
///     Marks the pipeline that protects outbound HTTP calls.
/// </summary>
public sealed class HttpPipeline;

/// <summary>
///     Marks the pipeline that protects calls to SQL Server made outside a unit of work.
/// </summary>
public sealed class DatabasePipeline;

/// <summary>
///     Marks the pipeline that protects publishing to the message broker.
/// </summary>
public sealed class BrokerPipeline;

/// <summary>
///     Carries the pipeline configured for one dependency.
/// </summary>
/// <typeparam name="TMarker">The marker type naming the dependency.</typeparam>
/// <remarks>
///     A marker type per dependency rather than one shared pipeline, so injecting
///     <c>ResiliencePipeline</c> is never ambiguous and a caller cannot accidentally get another
///     dependency's budget. Sharing one pipeline would mean the first registration wins and the other
///     two budgets are silently ignored.
/// </remarks>
public sealed class ResiliencePipelineFor<TMarker>(ResiliencePipeline pipeline)
{
    /// <summary>
    ///     The circuit breaker, retry and timeout to apply around calls to this dependency.
    /// </summary>
    public ResiliencePipeline Pipeline { get; } = pipeline;
}

/// <summary>
///     Builds and registers the Polly resilience pipelines for every outbound dependency.
/// </summary>
public static class ResilienceRegistration
{
    /// <summary>
    ///     Builds the pipeline for one dependency from its configured budget.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Polly nests in the order strategies are added, first added being outermost, so the order
    ///     below reads backwards: the circuit breaker is outermost so a dependency that is down is
    ///     rejected immediately, the retry is inside it so a transient failure is absorbed, and the
    ///     timeout is innermost so a single attempt is bounded.
    ///     </para>
    ///     <para>
    ///     The timeout being innermost is what makes the retry budget mean anything. Outermost, a
    ///     caller that had already exhausted its retries could still be abandoned by a timeout before
    ///     the breaker ever saw the failure.
    ///     </para>
    /// </remarks>
    /// <param name="options">The budget for this dependency.</param>
    public static ResiliencePipeline Build(DependencyResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(BuildCircuitBreaker(options));

        // Zero retries means the strategy is omitted, not configured with zero attempts. Polly treats a
        // retry budget of zero as a misconfiguration rather than as "do not retry", so a dependency
        // that should fail fast has to have no retry in the pipeline at all.
        if (options.MaxRetryAttempts > 0) builder.AddRetry(BuildRetry(options));

        return builder
            .AddTimeout(options.Timeout)
            .Build();
    }

    /// <summary>
    ///     The retry strategy for one dependency.
    /// </summary>
    /// <remarks>
    ///     Exposed separately from <see cref="Build" /> so the shape of the policy — exponential rather
    ///     than constant, capped, jittered — is inspectable without having to infer it from elapsed
    ///     time, which is a measurement the test suite would only get right on an idle machine.
    /// </remarks>
    /// <param name="options">The budget for this dependency.</param>
    public static RetryStrategyOptions BuildRetry(DependencyResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new RetryStrategyOptions
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            BackoffType = DelayBackoffType.Exponential,
            Delay = options.Delay,
            MaxDelay = options.MaxDelay,
            UseJitter = options.UseJitter,
            ShouldHandle = new PredicateBuilder()
                .Handle<Exception>(ex => ex is not OperationCanceledException)
        };
    }

    /// <summary>
    ///     The circuit breaker for one dependency.
    /// </summary>
    /// <param name="options">The budget for this dependency.</param>
    public static CircuitBreakerStrategyOptions BuildCircuitBreaker(DependencyResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new CircuitBreakerStrategyOptions
        {
            FailureRatio = options.FailureRatio,
            SamplingDuration = options.BreakDuration,
            MinimumThroughput = options.MinimumThroughput,
            BreakDuration = options.BreakDuration,
            ShouldHandle = new PredicateBuilder()
                .Handle<Exception>(ex => ex is not OperationCanceledException)
        };
    }

    /// <summary>
    ///     Registers one pipeline per dependency, each keyed by its marker type.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Configuration carrying the <c>Resilience</c> section.</param>
    public static IServiceCollection AddResiliencePipelines(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ResilienceOptions>(configuration.GetSection(ResilienceOptions.SectionName));

        var options = configuration.GetSection(ResilienceOptions.SectionName).Get<ResilienceOptions>()
                      ?? new ResilienceOptions();

        services.AddSingleton(new ResiliencePipelineFor<HttpPipeline>(Build(options.Http)));
        services.AddSingleton(new ResiliencePipelineFor<DatabasePipeline>(Build(options.Database)));
        services.AddSingleton(new ResiliencePipelineFor<BrokerPipeline>(Build(options.Broker)));

        return services;
    }

}

using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;
using PolicyManager.Middleware;

namespace PolicyManager.Services;

/// <summary>
///     Builds the rate-limiting policy from configuration.
/// </summary>
/// <remarks>
///     Kept out of <c>Program.cs</c> because the partition key needs the correlation-ID middleware to
///     have already run, and because the reasoning behind <em>which</em> key is the whole point of
///     the feature — a limit applied to the wrong identity is either useless or actively harmful.
/// </remarks>
public static class RateLimiting
{
    /// <summary>
    ///     The name of the global limiter, and of the named policy, so both are addressable and
    ///     overridable per endpoint with <c>[EnableRateLimiting]</c> and <c>[DisableRateLimiting]</c>.
    /// </summary>
    public const string PolicyName = "api";

    /// <summary>
    ///     Adds the rate limiter to the service collection.
    /// </summary>
    /// <remarks>
    ///     The limits are read from <see cref="IOptions{RateLimitOptions}" /> through the request's
    ///     own services rather than captured into a closure at registration. <see cref="IOptions{T}" />
    ///     resolves its section lazily against the <em>finished</em> configuration, so this picks up
    ///     a source that was added after <c>Program.cs</c> had already read the section — which is
    ///     what a test host, or any configuration provider registered late, does. Reading eagerly
    ///     instead silently applies the built-in defaults and the limit appears to do nothing.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>();

        services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var options = Options(context);

                // A no-op limiter rather than no middleware, so the pipeline shape is identical
                // whether or not the feature is on and the ordering constraints documented in
                // Program.cs hold in every environment.
                if (!options.Enabled)
                    return RateLimitPartition.GetNoLimiter(PartitionKey(context));

                return RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimitOrNull ?? int.MaxValue,
                        Window = options.WindowOrNull ?? TimeSpan.FromSeconds(60),
                        QueueLimit = options.QueueLimitOrNull ?? 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    });
            });

            limiter.OnRejected = (rejected, _) =>
            {
                var context = rejected.HttpContext;

                if (!Options(context).Enabled) return ValueTask.CompletedTask;

                context.Response.StatusCode = Options(context).RejectionStatusCode;

                // A bare 429 tells a client only that it was too fast, not for how long to slow down,
                // so without this it either retries immediately — turning a burst into sustained load
                // — or gives up.
                context.Response.Headers["Retry-After"] =
                    ((int)Options(context).EffectiveCooldown.TotalSeconds)
                    .ToString(CultureInfo.InvariantCulture);

                return ValueTask.CompletedTask;
            };
        });

        return services;
    }

    /// <summary>
    ///     Chooses the key a request's quota is counted against.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The correlation ID is preferred over the remote address because a limit keyed on the
    ///     address punishes everyone behind a shared egress — a corporate NAT, a mobile carrier, a CI
    ///     runner — for one caller's misbehaviour, and gives an attacker a single address to rotate.
    ///     The correlation ID is echoed on every response and is already sanitised to log-safe
    ///     characters by <see cref="CorrelationIdMiddleware" />.
    ///     </para>
    ///     <para>
    ///     It is not an authenticated identity and is not treated as one. A caller can mint a fresh
    ///     correlation ID per request and get a fresh quota, which is why the address remains the
    ///     fallback: the limiter degrades to per-connection rather than to per-nobody. Once
    ///     authentication supplies a stable subject, keying on that is strictly better and is the
    ///     obvious next step.
    ///     </para>
    /// </remarks>
    /// <param name="context">The current request.</param>
    /// <returns>The key the quota is counted against.</returns>
    public static string PartitionKey(HttpContext context)
        => context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var correlationId)
           && correlationId is string id
            ? id
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitOptions Options(HttpContext context)
        => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
}

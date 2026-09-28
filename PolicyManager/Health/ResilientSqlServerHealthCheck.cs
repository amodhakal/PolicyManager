using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using PolicyManager.Resilience;

namespace PolicyManager.Health;

/// <summary>
///     Wraps the SQL Server probe in the database resilience pipeline.
/// </summary>
/// <remarks>
///     <para>
///     The probe is the one database call the application makes on a timer, and the one most likely
///     to be made repeatedly while the database is down — an orchestrator polls readiness every few
///     seconds precisely when things are worst. Without a breaker each of those polls pays the full
///     timeout, and with a retry budget underneath it pays that several times over, so the check
///     aimed at detecting an outage becomes a contributor to it.
///     </para>
///     <para>
///     A decorator rather than a change to <see cref="SqlServerHealthCheck" />, because what the
///     check does is already right; what is missing is a policy around it, and a decorator says that
///     without coupling the check to Polly.
///     </para>
/// </remarks>
public sealed class ResilientSqlServerHealthCheck(
    IHealthCheck inner,
    ResiliencePipelineFor<DatabasePipeline> pipelines,
    ILogger<ResilientSqlServerHealthCheck> logger) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await pipelines.Pipeline.ExecuteAsync(
                async token => await inner.CheckHealthAsync(context, token),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A pipeline can fail for a reason the inner check never saw: an open circuit rejecting
            // the call without attempting it, or the retry budget being exhausted. Either way the
            // answer is the same, and the inner check's own redaction of the reason still applies.
            logger.LogError(ex, "The SQL Server health check was short-circuited or exhausted its budget.");

            return HealthCheckResult.Unhealthy(
                SqlServerHealthCheck.UnhealthyDescription,
                data: new Dictionary<string, object> { ["exceptionType"] = ex.GetType().Name });
        }
    }
}

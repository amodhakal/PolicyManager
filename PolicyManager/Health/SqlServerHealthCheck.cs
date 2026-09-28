using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using PolicyManager.Data;

namespace PolicyManager.Health;

/// <summary>
///     Readiness probe that proves the process can actually reach SQL Server, rather than merely that
///     it started.
/// </summary>
/// <remarks>
///     The check runs through the injected <see cref="AppDbContext" /> so it exercises the same
///     connection string, provider and credentials the rest of the application uses. A check that
///     opened its own <see cref="System.Data.Common.DbConnection" /> could pass while every real
///     request failed.
/// </remarks>
public class SqlServerHealthCheck(AppDbContext dbContext, ILogger<SqlServerHealthCheck> logger) : IHealthCheck
{
    /// <summary>
    ///     The statement sent to the server. It selects a constant rather than reading a table, so the
    ///     probe measures the connection — pool, network, authentication, server responsiveness — and
    ///     not whether a particular migration has been applied.
    /// </summary>
    private const string ProbeStatement = "SELECT 1";

    /// <summary>
    ///     The description returned for every failure, whatever went wrong.
    /// </summary>
    /// <remarks>
    ///     Deliberately constant. A provider exception names the server, the database, the login and
    ///     frequently the credential path, and this endpoint is unauthenticated, so putting the reason
    ///     in the description would hand that to anyone who can reach the port. The exception itself is
    ///     logged instead, where it is reachable through the correlation ID of the request that
    ///     triggered it.
    /// </remarks>
    public const string UnhealthyDescription = "The database is not reachable.";

    /// <summary>
    ///     Runs the probe.
    /// </summary>
    /// <remarks>
    ///     A connection opened from the pool still has to be validated, which is what makes this worth
    ///     doing on every call rather than only at startup: a connection that was valid when the
    ///     process started is routinely dead by the time a liveness probe first asks.
    /// </remarks>
    /// <param name="context">The context the check was registered under.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    ///     Healthy when the server answered the probe; otherwise Unhealthy with a constant description
    ///     and the exception type in <c>Data</c>, which is a name rather than a message.
    /// </returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Not CanConnectAsync alone: that only proves a socket opened, and a half-open or
            // credential-revoked connection can still pass it. A round trip is the real question.
            await dbContext.Database.ExecuteSqlRawAsync(ProbeStatement, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The SQL Server health check could not reach the database.");

            return HealthCheckResult.Unhealthy(
                UnhealthyDescription,
                data: new Dictionary<string, object> { ["exceptionType"] = ex.GetType().Name });
        }
    }
}

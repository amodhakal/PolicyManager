using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;
using PolicyManager.Data;

namespace PolicyManager.Services;

/// <summary>
///     Polls the transactional outbox on an adaptive interval and dispatches due messages.
/// </summary>
/// <remarks>
///     The loop does nothing but schedule: claiming, publishing, retry and dead-lettering all live in
///     <see cref="OutboxDispatcher" />. The interval adapts to the backlog — a short delay while there
///     is work, exponential backoff up to a ceiling while idle — so a busy outbox drains promptly
///     without a permanently idle instance querying the table several times a second.
/// </remarks>
public class OutboxProcessorBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxProcessorBackgroundService> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    /// <summary>
    ///     Polls until the host shuts down.
    /// </summary>
    /// <param name="stoppingToken">Token used to signal host shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox processor started.");

        var idleDelay = _options.MinIdleDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delivered = 0;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
                delivered = await dispatcher.DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed pass is expected while the database is restarting or unreachable. It must
                // not kill the host, and it counts as idle so the backoff grows instead of hammering.
                logger.LogError(ex, "Outbox dispatch pass failed.");
            }

            var delay = delivered > 0 ? _options.BusyDelay : idleDelay;
            if (delivered == 0) idleDelay = NextIdleDelay(idleDelay);

            // The delay sits outside the try above on purpose: awaiting it with a cancelled token
            // throws OperationCanceledException, and letting that escape ExecuteAsync surfaces as an
            // unhandled exception during host shutdown. Caught here so a normal stop is a clean exit.
            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Outbox processor stopped.");
    }

    private TimeSpan NextIdleDelay(TimeSpan current)
    {
        var next = current * 2;
        return next > _options.MaxIdleDelay ? _options.MaxIdleDelay : next;
    }
}

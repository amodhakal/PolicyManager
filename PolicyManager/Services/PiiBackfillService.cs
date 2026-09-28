using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;
using PolicyManager.Data;
using PolicyManager.Models;

namespace PolicyManager.Services;

/// <summary>
///     Converts policyholder email addresses written before encryption existed.
/// </summary>
/// <remarks>
///     <para>
///     This cannot be a migration. AES-GCM has no T-SQL equivalent, and the blind index is an HMAC
///     rather than a plain hash precisely because a plain hash of an email address is reversible by
///     brute force — so it cannot be computed in the database either. The rows have to be read and
///     written by the application, which is what this does.
///     </para>
///     <para>
///     Resumable and idempotent by construction: it selects only rows whose blind index is null, and
///     re-running it over an already-converted table finds nothing to do. Rows written by the
///     application from the moment this feature is deployed already have one, so a backfill that is
///     interrupted affects only the legacy rows and the next pass continues where it stopped.
///     </para>
///     <para>
///     It does not run to completion in one pass. Converting a whole table in a single transaction
///     would hold locks for as long as it takes and, if it failed partway, roll back all of it. Each
///     pass commits its own batch, so progress is durable and the table stays usable throughout.
///     </para>
/// </remarks>
public class PiiBackfillService(
    IServiceScopeFactory scopeFactory,
    IOptions<PiiOptions> options,
    ILogger<PiiBackfillService> logger)
{
    /// <summary>
    ///     How many rows one pass converts.
    /// </summary>
    /// <remarks>
    ///     Small enough that a pass is short and its locks are held briefly. This is a one-off
    ///     conversion of a table that grows slowly, so throughput is not the constraint; not blocking
    ///     anyone else is.
    /// </remarks>
    private const int BatchSize = 200;

    /// <summary>
    ///     The configuration section flag that controls whether the backfill runs.
    /// </summary>
    public const string RunOnStartupKey = "Pii:BackfillOnStartup";

    /// <summary>
    ///     Converts up to one batch of legacy rows.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>How many rows this pass converted.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // AsNoTracking on the read and explicit writes, so a large conversion does not accumulate a
        // change tracker entry per row for the whole table.
        var legacy = await context.PolicyHolders
            .AsNoTracking()
            .Where(h => h.EmailHash == null)
            .OrderBy(h => h.Id)
            .Select(h => new { h.Id, h.Email })
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (legacy.Count == 0) return 0;

        var converted = 0;

        foreach (var row in legacy)
        {
            // A row that cannot be read with the configured key is left alone rather than failed
            // over. A wrong key is an operator error that has to be noticed, and stopping the whole
            // backfill on it is how that happens; skipping it and logging loudly is how it gets
            // noticed without blocking every other row.
            try
            {
                await context.PolicyHolders
                    .Where(h => h.Id == row.Id)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(h => h.Email, PiiCipher.Current!.Encrypt(row.Email))
                            .SetProperty(h => h.EmailHash, PiiCipher.Current!.BlindIndex(row.Email)),
                        cancellationToken);
                converted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Could not convert the email address for policyholder {PolicyHolderId}. It is " +
                    "left unencrypted and the second-phase migration will refuse to apply until it " +
                    "is dealt with.",
                    row.Id);
            }
        }

        logger.LogInformation("Converted {Converted} policyholder email addresses to ciphertext.", converted);

        return converted;
    }

    /// <summary>
    ///     Converts legacy rows in the background until none are left.
    /// </summary>
    /// <param name="stoppingToken">Signalled when the host is shutting down.</param>
    public async Task RunUntilCompleteAsync(CancellationToken stoppingToken)
    {
        var pass = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            pass++;

            int converted;
            try
            {
                converted = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Backing off rather than giving up: a transient database problem during a
                // deployment should not turn into a permanently unconverted table that only a human
                // notices when the second-phase migration fails.
                logger.LogWarning(ex, "The PII backfill pass failed. Retrying in a minute.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            if (converted == 0)
            {
                if (pass > 1)
                    logger.LogInformation("The PII backfill is complete after {Passes} passes.", pass);

                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.Models;

namespace PolicyManager.Services;

/// <summary>
///     Issues the human-readable numbers the business identifies its records by.
/// </summary>
public interface IBusinessNumberGenerator
{
    /// <summary>
    ///     Reserves and returns the next number of the given kind for the current year.
    /// </summary>
    /// <param name="kind">The two-letter prefix, for example <c>POL</c>.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    ///     A number of the form <c>PREFIX-YYYY-NNNNNN</c>. Unique within the kind, and increasing
    ///     within the year.
    /// </returns>
    Task<string> NextAsync(string kind, CancellationToken cancellationToken = default);
}

/// <summary>
///     Issues sequential business numbers from a per-kind, per-year counter row.
/// </summary>
/// <remarks>
///     The reservation is a compare-and-swap on the counter's <c>RowVersion</c>: read the row, add
///     one, and write it with the version that was read. If another instance got there first the
///     write matches no rows, raises <see cref="DbUpdateConcurrencyException" />, and the number is
///     retried. This is contention, not failure, so it is retried rather than surfaced.
/// </remarks>
public class BusinessNumberGenerator(
    AppDbContext context,
    TimeProvider timeProvider,
    ILogger<BusinessNumberGenerator> logger) : IBusinessNumberGenerator
{
    /// <summary>
    ///     Prefix for policy numbers.
    /// </summary>
    public const string PolicyKind = "POL";

    /// <summary>
    ///     Prefix for claim numbers.
    /// </summary>
    public const string ClaimKind = "CLM";

    /// <summary>
    ///     How many times a lost race for the counter is retried before giving up.
    /// </summary>
    /// <remarks>
    ///     Generous, because each attempt is a couple of round trips and a genuine burst of
    ///     concurrent creates is exactly the case that must not fail. Beyond it the counter row is
    ///     contended to a degree that indicates a design problem rather than bad luck.
    /// </remarks>
    private const int MaxAttempts = 10;

    /// <summary>
    ///     Digits reserved for the ordinal, so numbers sort as text within a year.
    /// </summary>
    private const int OrdinalDigits = 6;

    /// <inheritdoc />
    public async Task<string> NextAsync(string kind, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var year = timeProvider.GetUtcNow().Year;

        for (var attempt = 1; ; attempt++)
        {
            var sequence = await FindAsync(kind, year, cancellationToken);

            if (sequence is null)
            {
                sequence = new BusinessNumberSequence { Kind = kind, Year = year, Current = 0 };
                context.BusinessNumberSequences.Add(sequence);
            }
            else
            {
                // Tracked from the read, so the write is already conditional on the version that
                // was read. Marked explicitly because the counter only changes on a retry that
                // happens to compute the same value, and EF would otherwise skip the statement.
                context.Entry(sequence)
                    .Property(nameof(BusinessNumberSequence.Current))
                    .IsModified = true;
            }

            var reserved = sequence.Current + 1;
            sequence.Current = reserved;

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return Format(kind, year, reserved);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Another instance reserved a number in between. The local copy is stale and the
                // row must be re-read, or the retry would fail identically forever.
                context.Entry(sequence).State = EntityState.Detached;

                logger.LogDebug(
                    "Lost the race for the {Kind} {Year} counter; retrying ({Attempt} of {Max}).",
                    kind, year, attempt, MaxAttempts);
            }
        }
    }

    /// <summary>
    ///     Renders a reserved ordinal as the number the business sees.
    /// </summary>
    /// <param name="kind">The two-letter prefix.</param>
    /// <param name="year">The calendar year the number belongs to.</param>
    /// <param name="ordinal">The reserved ordinal.</param>
    /// <returns>The formatted number.</returns>
    public static string Format(string kind, int year, long ordinal)
        => $"{kind}-{year:D4}-{ordinal.ToString($"D{OrdinalDigits}", System.Globalization.CultureInfo.InvariantCulture)}";

    private Task<BusinessNumberSequence?> FindAsync(string kind, int year, CancellationToken cancellationToken)
        => context.BusinessNumberSequences
            .FirstOrDefaultAsync(s => s.Kind == kind && s.Year == year, cancellationToken);
}

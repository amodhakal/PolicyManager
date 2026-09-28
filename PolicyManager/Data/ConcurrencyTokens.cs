using PolicyManager.Exceptions;
using PolicyManager.Models;

namespace PolicyManager.Data;

/// <summary>
///     Applies a caller-supplied concurrency token to a tracked entity.
/// </summary>
/// <remarks>
///     The token is only meaningful if it is the value the row had when the caller <em>read</em> it,
///     but the entity is loaded immediately before the write, so its current value is whatever the
///     row says now — a check against that can never fail. Overwriting the original value makes EF
///     Core compare against what the caller actually saw, which is the whole point of the token.
/// </remarks>
public static class ConcurrencyTokens
{
    /// <summary>
    ///     Rule identifier reported when a caller supplies a malformed concurrency token.
    /// </summary>
    public const string InvalidTokenRule = "invalid-row-version";

    /// <summary>
    ///     Restricts an update to the row state the caller last observed.
    /// </summary>
    /// <typeparam name="TEntity">The audited entity being updated.</typeparam>
    /// <param name="context">The context tracking the entity.</param>
    /// <param name="entity">The tracked entity about to be saved.</param>
    /// <param name="token">
    ///     The base64 <c>rowversion</c> the caller read, or null to leave the check to the value the
    ///     entity was loaded with.
    /// </param>
    /// <exception cref="BusinessRuleException">The token is not a valid base64 rowversion.</exception>
    public static void ApplyOriginalValue<TEntity>(
        this AppDbContext context,
        TEntity entity,
        string? token)
        where TEntity : class, IAuditableEntity
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        byte[] value;

        try
        {
            value = Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            // Ignored rather than coerced to an empty token. Silently dropping it would let a
            // caller believe they had optimistic-concurrency protection when the write would
            // succeed regardless of anyone else's changes.
            throw new BusinessRuleException(
                "The supplied rowVersion is not a valid concurrency token. Read the resource first " +
                "and send back the rowVersion exactly as it was returned.",
                InvalidTokenRule);
        }

        if (value.Length == 0)
            throw new BusinessRuleException(
                "The supplied rowVersion is empty. Read the resource first and send back the " +
                "rowVersion exactly as it was returned.",
                InvalidTokenRule);

        context.Entry(entity).Property(nameof(IAuditableEntity.RowVersion)).OriginalValue = value;
    }

    /// <summary>
    ///     Renders a stored concurrency token for inclusion in a response body.
    /// </summary>
    /// <param name="value">The stored token.</param>
    /// <returns>The base64 token, or null when the store has not assigned one yet.</returns>
    public static string? ToToken(byte[]? value)
        => value is null || value.Length == 0
            ? null
            : Convert.ToBase64String(value);
}

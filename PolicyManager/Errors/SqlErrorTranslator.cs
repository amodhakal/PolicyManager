using Microsoft.Data.SqlClient;

namespace PolicyManager.Errors;

/// <summary>
///     Maps SQL Server errors onto HTTP semantics.
/// </summary>
/// <remarks>
///     EF Core wraps provider exceptions in <see cref="Microsoft.EntityFrameworkCore.DbUpdateException" />,
///     so a constraint violation reaches the pipeline as an opaque wrapper. Translated here, a unique
///     index violation becomes a 409 the caller can act on rather than a 500 that reads like a server
///     fault. Keeping the mapping in one place means every endpoint reports these consistently.
/// </remarks>
public static class SqlErrorTranslator
{
    /// <summary>
    ///     A unique index or unique constraint was violated.
    /// </summary>
    private const int UniqueIndexViolation = 2601;

    /// <summary>
    ///     A unique constraint was violated on a unique index.
    /// </summary>
    private const int UniqueConstraintViolation = 2627;

    /// <summary>
    ///     A foreign key constraint was violated.
    /// </summary>
    private const int ForeignKeyViolation = 547;

    /// <summary>
    ///     A value did not fit the column it was written to.
    /// </summary>
    private const int StringOrBinaryDataTruncated = 8152;

    /// <summary>
    ///     An arithmetic operation overflowed the target type.
    /// </summary>
    private const int ArithmeticOverflow = 220;

    /// <summary>
    ///     The number of the innermost SQL Server error, if there is one.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns>The SQL Server error number, or null when the exception did not come from SQL Server.</returns>
    public static int? GetSqlErrorNumber(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException) return sqlException.Number;
        }

        return null;
    }

    /// <summary>
    ///     Determines whether a failure is a uniqueness violation.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns>True when a unique index or constraint was violated.</returns>
    public static bool IsUniqueViolation(Exception exception)
    {
        var number = GetSqlErrorNumber(exception);
        return number is UniqueIndexViolation or UniqueConstraintViolation;
    }

    /// <summary>
    ///     Determines whether a failure is a foreign key violation.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns>True when a foreign key constraint was violated.</returns>
    public static bool IsForeignKeyViolation(Exception exception)
        => GetSqlErrorNumber(exception) == ForeignKeyViolation;

    /// <summary>
    ///     Determines whether a failure is a value that does not fit its column.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns>True when a value overflowed or was truncated on write.</returns>
    public static bool IsColumnLimitViolation(Exception exception)
    {
        var number = GetSqlErrorNumber(exception);
        return number is StringOrBinaryDataTruncated or ArithmeticOverflow;
    }

    /// <summary>
    ///     Reads the constraint name out of a SQL Server error message, when the server reported one.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns>A constraint or index name, or null when none could be read.</returns>
    public static string? GetConstraintName(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not SqlException sqlException) continue;

            foreach (SqlError error in sqlException.Errors)
                if (!string.IsNullOrWhiteSpace(error.ConstraintName)) return error.ConstraintName;

            break;
        }

        return null;
    }
}

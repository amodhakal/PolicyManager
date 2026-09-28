using System.ComponentModel.DataAnnotations;

namespace PolicyManager.Models;

/// <summary>
///     The running counter behind a business number, for one number kind in one calendar year.
/// </summary>
/// <remarks>
///     <para>
///     A GUID is unique but unreadable: nobody can quote it down a telephone, spot a transposition,
///     or tell which of two policies was issued first. A sequential number is quotable and sortable,
///     and the year in it means an issue is traceable to a period without a lookup.
///     </para>
///     <para>
///     The counter is a row rather than a value derived from the table's own data so that allocation
///     is a single atomic increment. Deriving "highest number issued so far" from the rows themselves
///     would need a read followed by a write, and two concurrent inserts would both read the same
///     maximum and produce the same number. The row is guarded by a <see cref="RowVersion" />, so
///     exactly one of them wins and the loser retries — which is the same optimistic-concurrency
///     mechanism the rest of the schema uses, and is why this type is audited too.
///     </para>
/// </remarks>
public class BusinessNumberSequence : IAuditableEntity
{
    /// <summary>
    ///     The two-letter prefix identifying what the number is for, such as <c>POL</c>.
    /// </summary>
    [Required]
    [MaxLength(16)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    ///     The calendar year the counter applies to.
    /// </summary>
    /// <remarks>
    ///     Numbering restarts each year so a number stays short enough to read out and to write on a
    ///     form. Combined with <see cref="Kind" /> it is the primary key.
    /// </remarks>
    public int Year { get; set; }

    /// <summary>
    ///     How many numbers of this kind have been issued in this year.
    /// </summary>
    public long Current { get; set; }

    /// <inheritdoc />
    [Required]
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; set; } = [];
}

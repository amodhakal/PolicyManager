using System.ComponentModel.DataAnnotations;

namespace PolicyManager.Models;

/// <summary>
///     Represents a policyholder who owns insurance policies.
/// </summary>
public class PolicyHolder : IAuditableEntity
{
    /// <summary>
    ///     The unique identifier of the policyholder.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     The first name of the policyholder.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    ///     The last name of the policyholder.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    ///     The email address of the policyholder, stored encrypted.
    /// </summary>
    /// <remarks>
    ///     The CLR property is a plain string and the encryption happens in the EF value converter, so
    ///     nothing above the persistence layer has to know the value is ciphertext. It is never null
    ///     and never empty: an absent address is not a state this system has, and a nullable encrypted
    ///     column is one more thing to get wrong in a query.
    /// </remarks>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    ///     A keyed hash of the email address.
    /// </summary>
    /// <remarks>
    ///     Unique once the backfill has completed and the second-phase migration has applied the
    ///     constraint. Null for rows written before this feature was deployed, until the backfill
    ///     converts them.
    /// </remarks>
    /// <remarks>
    ///     The database can only enforce uniqueness over a deterministic value, and ciphertext is
    ///     deliberately randomised, so duplicate detection runs against this instead. It is written
    ///     from the same input in the same save as <see cref="Email" /> and is never exposed: it is an
    ///     equality key, not a weaker copy of the address.
    /// </remarks>
    public string? EmailHash { get; set; }

    /// <summary>
    ///     Whether the policyholder has been soft-deleted.
    /// </summary>
    /// <remarks>
    ///     A soft delete keeps the row and hides it, so the policyholder's policies and the claims
    ///     filed against them survive and can be restored. A hard delete would cascade from the
    ///     foreign keys and take that claim history with it.
    ///     <para>
    ///         Every read path filters on this, so a deleted policyholder disappears from the API
    ///         without leaving the table. <c>DeletionDate</c> records when that happened for the audit
    ///         trail.
    ///     </para>
    /// </remarks>
    public bool IsDeleted { get; set; }

    /// <summary>
    ///     When the policyholder was soft-deleted, or null while they are active.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="CreatedAt" /> so "never existed" and "was removed on this date"
    ///     stay separable, which matters when a record is restored.
    /// </remarks>
    public DateTime? DeletionDate { get; set; }

    /// <summary>
    ///     The collection of policies owned by this policyholder.
    /// </summary>
    public ICollection<Policy> Policies { get; set; } = new List<Policy>();

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
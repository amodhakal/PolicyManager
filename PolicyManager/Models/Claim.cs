using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PolicyManager.Models.Enums;

namespace PolicyManager.Models;

/// <summary>
///     Represents an insurance claim submitted by a policy holder.
/// </summary>
public class Claim : IAuditableEntity
{
    /// <summary>
    ///     The unique identifier of the claim.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     The unique claim number generated for this claim.
    /// </summary>
    /// <remarks>
    ///     A sequential number such as <c>CLM-2026-000001</c>, reserved by
    ///     <see cref="Services.IBusinessNumberGenerator" />. Left without a default so that a write
    ///     path which forgets to reserve one fails on the <c>NOT NULL</c> constraint instead of
    ///     quietly persisting another unquotable GUID.
    /// </remarks>
    [Required]
    [MaxLength(50)]
    public string ClaimNumber { get; set; } = string.Empty;

    /// <summary>
    ///     The description of the claim.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     The claim amount requested.
    /// </summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    /// <summary>
    ///     The current status of the claim.
    /// </summary>
    [Required]
    public ClaimStatus Status { get; set; } = ClaimStatus.Pending;

    /// <summary>
    ///     The date and time when the claim was filed.
    /// </summary>
    public DateTime FiledAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Free-text notes recorded by the adjuster who decided the claim. Null until adjudicated.
    /// </summary>
    /// <remarks>
    ///     Nullable rather than an empty string so "not yet adjudicated" and "adjudicated with no
    ///     notes" stay distinguishable. That distinction matters when auditing who decided what.
    /// </remarks>
    [MaxLength(1000)]
    public string? AdjusterNotes { get; set; }

    /// <summary>
    ///     When the claim was adjudicated. Null while the claim is still pending.
    /// </summary>
    public DateTime? DecisionDate { get; set; }

    /// <summary>
    ///     The identifier of the adjuster who made the decision. Null while the claim is still pending.
    /// </summary>
    [MaxLength(100)]
    public string? DecidedBy { get; set; }

    /// <summary>
    ///     The policy associated with this claim.
    /// </summary>
    [Required]
    public int PolicyId { get; set; }

    /// <summary>
    ///     Whether the claim has been soft-deleted.
    /// </summary>
    /// <remarks>
    ///     A soft delete keeps the row and hides it, so the amount it reserved against its policy's
    ///     coverage stops being reserved while the decision behind it stays on the record. Every read
    ///     path filters on this, so a deleted claim is absent from the API without leaving the table.
    /// </remarks>
    public bool IsDeleted { get; set; }

    /// <summary>
    ///     When the claim was soft-deleted, or null while it is active.
    /// </summary>
    public DateTime? DeletionDate { get; set; }

    /// <summary>
    ///     The policy associated with this claim.
    /// </summary>
    public Policy? Policy { get; set; }

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
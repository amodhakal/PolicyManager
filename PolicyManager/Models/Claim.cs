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
    [Required]
    [MaxLength(50)]
    public string ClaimNumber { get; set; } = Guid.NewGuid().ToString();

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
    ///     The unique identifier of the policy associated with this claim.
    /// </summary>
    [Required]
    public int PolicyId { get; set; }

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
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PolicyManager.Models.Enums;

namespace PolicyManager.Models;

/// <summary>
///     Represents an insurance claim submitted by a policy holder.
/// </summary>
public class Claim
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
    ///     The unique identifier of the policy associated with this claim.
    /// </summary>
    [Required]
    public int PolicyId { get; set; }

    /// <summary>
    ///     The policy associated with this claim.
    /// </summary>
    public Policy? Policy { get; set; }
}
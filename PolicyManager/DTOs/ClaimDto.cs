using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for claim information.
/// </summary>
public class ClaimDto
{
    /// <summary>
    ///     The unique identifier of the claim.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     The unique claim number generated for the claim.
    /// </summary>
    public string ClaimNumber { get; set; } = string.Empty;

    /// <summary>
    ///     The unique identifier of the policy associated with the claim.
    /// </summary>
    public int PolicyId { get; set; }

    /// <summary>
    ///     The description of the claim.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     The claim amount in the currency specified by the policy.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    ///     The current status of the claim.
    /// </summary>
    public ClaimStatus Status { get; set; }

    /// <summary>
    ///     The date and time when the claim was filed.
    /// </summary>
    public DateTime FiledAt { get; set; }

    /// <summary>
    ///     The date and time when the claim was adjudicated, or null while it is still pending.
    /// </summary>
    public DateTime? DecisionDate { get; set; }

    /// <summary>
    ///     The identifier of the adjuster who decided the claim, or null while it is still pending.
    /// </summary>
    public string? DecidedBy { get; set; }

    /// <summary>
    ///     Notes recorded by the adjuster, or null when none were recorded.
    /// </summary>
    public string? AdjusterNotes { get; set; }
}
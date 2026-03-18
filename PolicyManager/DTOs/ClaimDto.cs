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
    ///     The unique identifier of the policy associated with the claim.
    /// </summary>
    public int PolicyId { get; set; }

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
}
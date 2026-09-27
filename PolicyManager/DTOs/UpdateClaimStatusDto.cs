using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for updating the status of an existing claim.
/// </summary>
public class UpdateClaimStatusDto
{
    /// <summary>
    ///     The new status of the claim.
    /// </summary>
    public ClaimStatus Status { get; set; }
}
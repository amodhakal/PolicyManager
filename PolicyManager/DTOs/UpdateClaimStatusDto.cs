using System.ComponentModel.DataAnnotations;
using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for updating the status of an existing claim.
/// </summary>
/// <remarks>
///     <see cref="Status" /> is nullable so an omitted field is a 400 rather than a silent
///     <c>Pending</c>: <see cref="ClaimStatus" /> has no unknown member, so its default is a real
///     adjudication outcome, and a PATCH that says nothing would otherwise re-open a decided claim.
/// </remarks>
public class UpdateClaimStatusDto
{
    /// <summary>
    ///     The new status of the claim. Required.
    /// </summary>
    [Required]
    public ClaimStatus? Status { get; set; }
}

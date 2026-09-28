using System.ComponentModel.DataAnnotations;
using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for adjudicating an existing claim.
/// </summary>
/// <remarks>
///     <see cref="Status" /> is nullable so an omitted field is a 400 rather than a silent
///     <c>Pending</c>: <see cref="ClaimStatus" /> has no unknown member, so its default is a real
///     adjudication outcome, and a PATCH that says nothing would otherwise re-open a decided claim.
///     <para>
///     <see cref="Notes" /> and <see cref="DecidedBy" /> are optional so a minimal
///     <c>{"status":"Approved"}</c> still works, but an adjuster can record who decided what. The
///     decision timestamp is set by the server, never by the caller, so it cannot be backdated.
///     </para>
/// </remarks>
public class UpdateClaimStatusDto
{
    /// <summary>
    ///     The new status of the claim. Required.
    /// </summary>
    [Required]
    public ClaimStatus? Status { get; set; }

    /// <summary>
    ///     Notes recorded against the decision.
    /// </summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>
    ///     The identifier of the adjuster making the decision.
    /// </summary>
    [MaxLength(100)]
    public string? DecidedBy { get; set; }

    /// <summary>
    ///     The <c>rowVersion</c> read from this claim, or null to write unconditionally.
    /// </summary>
    /// <remarks>
    ///     Meaningful here because adjudication is the step two operators are most likely to reach
    ///     at once: without it the second decision quietly replaces the first.
    /// </remarks>
    public string? RowVersion { get; set; }
}

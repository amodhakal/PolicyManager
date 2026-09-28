using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for policy information.
/// </summary>
public class PolicyDto
{
    /// <summary>
    ///     The unique identifier of the policy.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     The unique policy number assigned to this policy.
    /// </summary>
    public string PolicyNumber { get; set; } = string.Empty;

    /// <summary>
    ///     The premium amount for the policy.
    /// </summary>
    public decimal Premium { get; set; }

    /// <summary>
    ///     The current status of the policy.
    /// </summary>
    public PolicyStatus Status { get; set; }

    /// <summary>
    ///     The full name of the policyholder.
    /// </summary>
    public string PolicyholderName { get; set; } = string.Empty;

    /// <summary>
    ///     The unique identifier of the policyholder.
    /// </summary>
    public int PolicyHolderId { get; set; }

    /// <summary>
    ///     The type of insurance policy.
    /// </summary>
    public PolicyType Type { get; set; }

    /// <summary>
    ///     The maximum total claimable against this policy, or null when there is no stated limit.
    /// </summary>
    public decimal? CoverageLimit { get; set; }

    /// <summary>
    ///     The start date of the policy coverage.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    ///     The end date of the policy coverage.
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    ///     When the record was last modified, or null while it has never been modified.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    ///     Who last modified the record, or null while it has never been modified.
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    ///     The record's concurrency token, as base64. Send it back on an update to make the write
    ///     conditional on nothing having changed since the record was read.
    /// </summary>
    /// <remarks>
    ///     Opaque and server-generated. A mismatch is reported as <c>409 Conflict</c> rather than
    ///     silently overwriting the other writer's change.
    /// </remarks>
    public string? RowVersion { get; set; }
}

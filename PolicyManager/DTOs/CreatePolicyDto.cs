using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for creating a new policy.
/// </summary>
public class CreatePolicyDto
{
    /// <summary>
    ///     The premium amount for the policy.
    /// </summary>
    public decimal Premium { get; set; }

    /// <summary>
    ///     The unique identifier of the policyholder.
    /// </summary>
    public int PolicyHolderId { get; set; }

    /// <summary>
    ///     The type of insurance policy.
    /// </summary>
    public PolicyType Type { get; set; }

    /// <summary>
    ///     The start date of the policy coverage.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    ///     The end date of the policy coverage.
    /// </summary>
    public DateTime EndDate { get; set; }
}
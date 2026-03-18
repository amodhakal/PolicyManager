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
    public string PolicyNumber { get; set; }

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
    public string PolicyholderName { get; set; }
}
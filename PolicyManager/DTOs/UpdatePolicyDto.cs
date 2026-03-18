using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for updating an existing policy.
/// </summary>
public class UpdatePolicyDto
{
    /// <summary>
    ///     The updated premium amount for the policy.
    /// </summary>
    public decimal Premium { get; set; }

    /// <summary>
    ///     The updated status of the policy.
    /// </summary>
    public PolicyStatus Status { get; set; }
}
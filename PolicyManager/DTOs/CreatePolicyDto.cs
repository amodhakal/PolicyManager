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
}
namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for creating a new claim.
/// </summary>
public class CreateClaimDto
{
    /// <summary>
    ///     The unique identifier of the policy to file the claim against.
    /// </summary>
    public int PolicyId { get; set; }

    /// <summary>
    ///     The claim amount requested.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    ///     The description of the claim.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
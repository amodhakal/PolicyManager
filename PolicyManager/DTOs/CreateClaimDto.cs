using System.ComponentModel.DataAnnotations;

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
    /// <remarks>
    ///     Bounded to the storable range of the <c>decimal(10,2)</c> column: a claim must be at least
    ///     one cent, and the maximum is 99,999,999.99 because that is the largest value the column can
    ///     hold, so anything above it cannot be persisted.
    /// </remarks>
    [Range(typeof(decimal), "0.01", "99999999.99")]
    public decimal Amount { get; set; }

    /// <summary>
    ///     The description of the claim.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
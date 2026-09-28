using System.ComponentModel.DataAnnotations;
using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for creating a new policy.
/// </summary>
/// <remarks>
///     <see cref="Type" /> is nullable so that an omitted field is distinguishable from an explicit
///     zero value. <see cref="PolicyType" /> has no "unknown" member, so its default of
///     <see cref="PolicyType.Auto" /> is a perfectly valid choice; binding an omitted field to it
///     would silently issue an Auto policy when the caller asked for something else or asked for
///     nothing.
/// </remarks>
public class CreatePolicyDto : IValidatableObject
{
    /// <summary>
    ///     The premium amount for the policy.
    /// </summary>
    /// <remarks>
    ///     Bounded to the storable range of the <c>decimal(10,2)</c> column: a premium must be at least
    ///     one cent, and the maximum is 99,999,999.99 so that an out-of-range value is rejected with a
    ///     400 instead of failing as a database overflow.
    /// </remarks>
    [Range(typeof(decimal), "0.01", "99999999.99")]
    public decimal Premium { get; set; }

    /// <summary>
    ///     The unique identifier of the policyholder.
    /// </summary>
    public int PolicyHolderId { get; set; }

    /// <summary>
    ///     The type of insurance policy. Required.
    /// </summary>
    [Required]
    public PolicyType? Type { get; set; }

    /// <summary>
    ///     The start date of the policy coverage.
    /// </summary>
    [Required]
    public DateTime StartDate { get; set; }

    /// <summary>
    ///     The end date of the policy coverage.
    /// </summary>
    [Required]
    public DateTime EndDate { get; set; }

    /// <summary>
    ///     Validates the cross-property constraints that attributes cannot express.
    /// </summary>
    /// <param name="validationContext">The context of the validation being performed.</param>
    /// <returns>
    ///     A result when the end date does not fall strictly after the start date; otherwise an empty
    ///     collection.
    /// </returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate <= StartDate)
        {
            yield return new ValidationResult(
                "EndDate must be later than StartDate.",
                [nameof(StartDate), nameof(EndDate)]);
        }
    }
}

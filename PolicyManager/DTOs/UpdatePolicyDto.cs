using System.ComponentModel.DataAnnotations;
using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for updating an existing policy.
/// </summary>
/// <remarks>
///     Both properties are nullable so that an omitted field means "leave this alone" rather than
///     "set it to the default". <see cref="PolicyStatus" /> has no unknown member, so its default of
///     <see cref="PolicyStatus.Active" /> is a real value; binding omission to it meant a caller
///     updating only the premium silently reinstated a cancelled policy. <see cref="decimal" /> is
///     the same problem with a worse outcome, defaulting to <c>0</c>.
/// </remarks>
public class UpdatePolicyDto : IValidatableObject
{
    /// <summary>
    ///     The updated premium amount, or null to leave the current premium unchanged.
    /// </summary>
    [Range(typeof(decimal), "0.01", "99999999.99")]
    public decimal? Premium { get; set; }

    /// <summary>
    ///     The updated status, or null to leave the current status unchanged.
    /// </summary>
    public PolicyStatus? Status { get; set; }

    /// <summary>
    ///     The <c>rowVersion</c> read from this policy, or null to write unconditionally.
    /// </summary>
    /// <remarks>
    ///     Does not count towards the "at least one change" rule: a token on its own is not a
    ///     change, and accepting it alone would report a no-op back to the caller as success.
    /// </remarks>
    public string? RowVersion { get; set; }

    /// <summary>
    ///     Validates that the request asks for at least one change.
    /// </summary>
    /// <param name="validationContext">The context of the validation being performed.</param>
    /// <returns>
    ///     A result when neither field is supplied, which would otherwise be a silent no-op reported
    ///     to the caller as success; otherwise an empty collection.
    /// </returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Premium is null && Status is null)
        {
            yield return new ValidationResult(
                "Supply at least one of 'premium' or 'status'.",
                [nameof(Premium), nameof(Status)]);
        }
    }
}

using System.ComponentModel.DataAnnotations;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for updating an existing policyholder.
/// </summary>
/// <remarks>
///     Every property is nullable so an omitted field means "leave this alone" rather than "set it to
///     empty". A non-nullable string defaults to <see cref="string.Empty" />, so binding an update
///     that touches only the last name would otherwise blank the first name and the email - which
///     also drops the holder out of the unique email index's conflict checks by making the address
///     something no caller could have registered.
/// </remarks>
public class UpdatePolicyHolderDto : IValidatableObject
{
    /// <summary>
    ///     The updated first name, or null to leave the current first name unchanged.
    /// </summary>
    [MaxLength(100)]
    public string? FirstName { get; set; }

    /// <summary>
    ///     The updated last name, or null to leave the current last name unchanged.
    /// </summary>
    [MaxLength(100)]
    public string? LastName { get; set; }

    /// <summary>
    ///     The updated email address, or null to leave the current address unchanged.
    /// </summary>
    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>
    ///     Validates that the request asks for at least one change, and that any supplied field is
    ///     actually populated.
    /// </summary>
    /// <remarks>
    ///     An empty string is a request to blank a required column, not an omission: it would
    ///     overwrite the stored value and be rejected by the column's own constraints far away from
    ///     the request that caused it. Rejecting it here names the field instead.
    /// </remarks>
    /// <param name="validationContext">The context of the validation being performed.</param>
    /// <returns>
    ///     A result for a request that changes nothing or supplies an empty field; otherwise an empty
    ///     collection.
    /// </returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FirstName is null && LastName is null && Email is null)
        {
            yield return new ValidationResult(
                "Supply at least one of 'firstName', 'lastName' or 'email'.",
                [nameof(FirstName), nameof(LastName), nameof(Email)]);

            yield break;
        }

        foreach (var (name, value) in new[]
                 {
                     (nameof(FirstName), FirstName),
                     (nameof(LastName), LastName),
                     (nameof(Email), Email)
                 })
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
                yield return new ValidationResult($"'{name}' cannot be blank.", [name]);
        }
    }
}

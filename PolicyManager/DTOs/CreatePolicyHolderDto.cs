using System.ComponentModel.DataAnnotations;

namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for creating a new policyholder.
/// </summary>
public class CreatePolicyHolderDto
{
    /// <summary>
    ///     The first name of the policyholder.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    ///     The last name of the policyholder.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    ///     The email address of the policyholder.
    /// </summary>
    /// <remarks>
    ///     Bounded at 254 characters, the longest address that can arrive over SMTP. This is not
    ///     only a validation nicety: the column stores AES-GCM ciphertext of the address, which is
    ///     about 1.5x the input plus a nonce and a tag, so the column cannot be sized without a
    ///     bound on the input.
    /// </remarks>
    [Required]
    [EmailAddress]
    [MaxLength(254)]
    public string Email { get; set; } = string.Empty;
}
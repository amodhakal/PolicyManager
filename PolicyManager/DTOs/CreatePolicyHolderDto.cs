namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for creating a new policyholder.
/// </summary>
public class CreatePolicyHolderDto
{
    /// <summary>
    ///     The first name of the policyholder.
    /// </summary>
    public string FirstName { get; set; }

    /// <summary>
    ///     The last name of the policyholder.
    /// </summary>
    public string LastName { get; set; }

    /// <summary>
    ///     The email address of the policyholder.
    /// </summary>
    public string Email { get; set; }
}
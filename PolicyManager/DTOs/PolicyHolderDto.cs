namespace PolicyManager.DTOs;

/// <summary>
///     Data transfer object for policyholder information.
/// </summary>
public class PolicyHolderDto
{
    /// <summary>
    ///     The unique identifier of the policyholder.
    /// </summary>
    public int Id { get; set; }

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
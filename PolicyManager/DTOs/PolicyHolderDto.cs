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
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    ///     The last name of the policyholder.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    ///     The email address of the policyholder.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    ///     When the record was last modified, or null while it has never been modified.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    ///     Who last modified the record, or null while it has never been modified.
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    ///     The record's concurrency token, as base64. Send it back on an update to make the write
    ///     conditional on nothing having changed since the record was read.
    /// </summary>
    /// <remarks>
    ///     Opaque and server-generated. A mismatch is reported as <c>409 Conflict</c> rather than
    ///     silently overwriting the other writer's change.
    /// </remarks>
    public string? RowVersion { get; set; }
}

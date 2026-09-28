using System.ComponentModel.DataAnnotations;

namespace PolicyManager.Models;

/// <summary>
///     Represents a policyholder who owns insurance policies.
/// </summary>
public class PolicyHolder : IAuditableEntity
{
    /// <summary>
    ///     The unique identifier of the policyholder.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     The first name of the policyholder.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    ///     The last name of the policyholder.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    ///     The email address of the policyholder.
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    ///     The collection of policies owned by this policyholder.
    /// </summary>
    public ICollection<Policy> Policies { get; set; } = new List<Policy>();

    /// <inheritdoc />
    [Required]
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; set; } = [];
}
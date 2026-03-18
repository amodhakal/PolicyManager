using System.ComponentModel.DataAnnotations;

namespace PolicyManager.Models;

/// <summary>
///     Represents a policyholder who owns insurance policies.
/// </summary>
public class PolicyHolder
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
    ///     The date and time when the policyholder was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     The collection of policies owned by this policyholder.
    /// </summary>
    public ICollection<Policy> Policies { get; set; } = new List<Policy>();
}
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PolicyManager.Models.Enums;

namespace PolicyManager.Models;

/// <summary>
///     Represents an insurance policy held by a policyholder.
/// </summary>
public class Policy
{
    /// <summary>
    ///     The unique identifier of the policy.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     The unique policy number assigned to this policy.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string PolicyNumber { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    ///     The type of insurance policy.
    /// </summary>
    [Required]
    public PolicyType Type { get; set; } = PolicyType.Auto;

    /// <summary>
    ///     The current status of the policy.
    /// </summary>
    [Required]
    public PolicyStatus Status { get; set; } = PolicyStatus.Active;

    /// <summary>
    ///     The premium amount for the policy.
    /// </summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal Premium { get; set; }

    /// <summary>
    ///     The start date of the policy coverage.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    ///     The end date of the policy coverage.
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    ///     The unique identifier of the policyholder.
    /// </summary>
    [Required]
    public int PolicyHolderId { get; set; }

    /// <summary>
    ///     The policyholder who owns this policy.
    /// </summary>
    public PolicyHolder? PolicyHolder { get; set; }

    /// <summary>
    ///     The collection of claims associated with this policy.
    /// </summary>
    public ICollection<Claim> Claims { get; set; } = new List<Claim>();
}
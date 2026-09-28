using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PolicyManager.Models.Enums;

namespace PolicyManager.Models;

/// <summary>
///     Represents an insurance policy held by a policyholder.
/// </summary>
public class Policy : IAuditableEntity
{
    /// <summary>
    ///     The unique identifier of the policy.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     The unique policy number assigned to this policy.
    /// </summary>
    /// <remarks>
    ///     A sequential number such as <c>POL-2026-000001</c>, reserved by
    ///     <see cref="Services.IBusinessNumberGenerator" />. Left without a default so that a write
    ///     path which forgets to reserve one fails on the <c>NOT NULL</c> constraint instead of
    ///     quietly persisting another unquotable GUID.
    /// </remarks>
    [Required]
    [MaxLength(50)]
    public string PolicyNumber { get; set; } = string.Empty;

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
    [Required]
    [Column(TypeName = "decimal(10,2)")]
    public decimal Premium { get; set; }

    /// <summary>
    ///     The maximum total that may be claimed against this policy, or null for no stated limit.
    /// </summary>
    /// <remarks>
    ///     Deliberately separate from <see cref="Premium" />, which is what the holder pays. Conflating
    ///     them would cap a policy's payout at its price, which is not what either number means. Null
    ///     means unlimited, which is a legitimate configuration: a policy with no stated limit should
    ///     accept any claim, not reject every one of them.
    /// </remarks>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? CoverageLimit { get; set; }

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
using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     The premium report: the business on the books, distributed across policy types.
/// </summary>
/// <remarks>
///     <see cref="Types" /> carries one row per policy type whether or not any policy of that type
///     exists, so the shape of the report does not change as data arrives. Premiums are counted for
///     every policy on the books, cancelled and expired included, because the figure describes what
///     was written rather than what is still in force.
/// </remarks>
public class PremiumByTypeReportDto
{
    /// <summary>
    ///     The policy types, in declaration order, each with its policy count and total premium.
    /// </summary>
    public IReadOnlyList<PolicyTypeTotalDto> Types { get; set; } = Array.Empty<PolicyTypeTotalDto>();

    /// <summary>
    ///     The number of policies across every type.
    /// </summary>
    public long TotalPolicies { get; set; }

    /// <summary>
    ///     The premium across every type.
    /// </summary>
    public decimal TotalPremium { get; set; }
}

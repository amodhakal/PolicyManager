using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     One row of the premium report: the business written in a single policy type.
/// </summary>
public class PolicyTypeTotalDto
{
    /// <summary>
    ///     The policy type this row covers.
    /// </summary>
    public PolicyType Type { get; set; }

    /// <summary>
    ///     How many policies of this type are on the books.
    /// </summary>
    public long PolicyCount { get; set; }

    /// <summary>
    ///     The premium they carry in total.
    /// </summary>
    public decimal TotalPremium { get; set; }

    /// <summary>
    ///     The average premium per policy, or zero when there are none.
    /// </summary>
    public decimal AveragePremium { get; set; }
}

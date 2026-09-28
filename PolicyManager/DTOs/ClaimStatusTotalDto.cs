using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     One row of the open-claims report: the claims sitting in a single status and what they are for.
/// </summary>
public class ClaimStatusTotalDto
{
    /// <summary>
    ///     The claim status this row counts.
    /// </summary>
    public ClaimStatus Status { get; set; }

    /// <summary>
    ///     How many open claims sit in this status.
    /// </summary>
    public long ClaimCount { get; set; }

    /// <summary>
    ///     The total amount claimed by them.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    ///     The average amount per claim, or zero when there are none.
    /// </summary>
    public decimal AverageAmount { get; set; }
}

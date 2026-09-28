using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

/// <summary>
///     The open-claims report: what is outstanding, and how it is distributed across claim statuses.
/// </summary>
/// <remarks>
///     <see cref="Statuses" /> carries one row per open status whether or not any claim currently sits
///     in it, so a caller can chart the same shape before the first claim is filed as after. Denied
///     claims are not open and are not reported here.
/// </remarks>
public class OpenClaimsByStatusReportDto
{
    /// <summary>
    ///     The open claim statuses, in declaration order, each with its count and total amount.
    /// </summary>
    public IReadOnlyList<ClaimStatusTotalDto> Statuses { get; set; } = Array.Empty<ClaimStatusTotalDto>();

    /// <summary>
    ///     The number of open claims across every open status.
    /// </summary>
    public long TotalOpenClaims { get; set; }

    /// <summary>
    ///     The amount those open claims are for, across every open status.
    /// </summary>
    public decimal TotalOpenAmount { get; set; }
}

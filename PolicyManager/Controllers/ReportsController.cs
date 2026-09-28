using Microsoft.AspNetCore.Mvc;
using PolicyManager.DTOs;
using PolicyManager.Services;

namespace PolicyManager.Controllers;

/// <summary>
///     Controller for the reporting queries.
/// </summary>
/// <remarks>
///     Read-only. Every report is answered from the database on demand, with no caching: a report whose
///     numbers are quietly stale is worse than one that costs a query, and each is a single grouped
///     read over an indexed column.
/// </remarks>
[ApiController]
[Route("api/reports")]
public class ReportsController(IReportsService reportsService) : ControllerBase
{
    /// <summary>
    ///     Counts the open claims in each claim status, and totals them.
    /// </summary>
    /// <remarks>
    ///     A claim is open while it can still become money owed: pending or approved. Denied claims are
    ///     closed and are not reported. Every open status appears in the result even when no claim
    ///     currently sits in it, so the shape of the report does not change with the data.
    /// </remarks>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The per-status breakdown and the overall totals.</returns>
    /// <response code="200">Returns the open-claims report.</response>
    [HttpGet("open-claims-by-status")]
    public async Task<ActionResult<OpenClaimsByStatusReportDto>> GetOpenClaimsByStatus(
        CancellationToken cancellationToken)
    {
        var report = await reportsService.GetOpenClaimsByStatus(cancellationToken);
        return Ok(report);
    }

    /// <summary>
    ///     Totals the premium written in each policy type.
    /// </summary>
    /// <remarks>
    ///     Premium is counted for every policy on the books, cancelled and expired included, because
    ///     the figure answers what has been written rather than what is still in force. Every policy
    ///     type appears in the result even when no policy of that type exists.
    /// </remarks>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The per-type breakdown and the overall totals.</returns>
    /// <response code="200">Returns the premium report.</response>
    [HttpGet("premium-by-type")]
    public async Task<ActionResult<PremiumByTypeReportDto>> GetPremiumByType(CancellationToken cancellationToken)
    {
        var report = await reportsService.GetPremiumByType(cancellationToken);
        return Ok(report);
    }

    /// <summary>
    ///     Retrieves a page of policyholders with their open claim amount weighed against their
    ///     premium.
    /// </summary>
    /// <remarks>
    ///     Supports <c>?page=</c>, <c>?pageSize=</c>, <c>?sortBy=</c> and <c>?descending=</c>. Sorts by
    ///     <c>claimsRatio</c> (the default), <c>totalPremium</c>, <c>openClaimCount</c>,
    ///     <c>totalOpenClaimAmount</c>, <c>policyCount</c> or <c>policyHolderId</c>. The page size is
    ///     capped at <see cref="PaginationQuery.MaxPageSize" /> and an unrecognised sort key falls back
    ///     to the ratio.
    ///     <para>
    ///         Only policyholders who own at least one policy appear: a holder with no premium has no
    ///         ratio to report, and a row of zeros would bury the holders who do.
    ///     </para>
    /// </remarks>
    /// <param name="pagination">The requested page, page size and sort.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of policyholder ratios, worst first by default.</returns>
    /// <response code="200">Returns the requested page of ratios and the total matching count.</response>
    [HttpGet("claims-ratio-per-holder")]
    public async Task<ActionResult<PagedResult<HolderClaimsRatioDto>>> GetClaimsRatioPerHolder(
        [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var report = await reportsService.GetClaimsRatioPerHolder(pagination, cancellationToken);
        return Ok(report);
    }
}

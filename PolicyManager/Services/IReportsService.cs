using PolicyManager.DTOs;

namespace PolicyManager.Services;

/// <summary>
///     Interface for the reporting queries.
/// </summary>
/// <remarks>
///     Separate from the entity services because a report reads across holders, policies and claims at
///     once, and because nothing in it writes: there is no outbox message to record and no cache entry
///     to keep in step with.
/// </remarks>
public interface IReportsService
{
    /// <summary>
    ///     Counts the open claims in each open status, and totals them.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The per-status breakdown and the overall totals.</returns>
    public Task<OpenClaimsByStatusReportDto> GetOpenClaimsByStatus(
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Totals the premium written in each policy type.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The per-type breakdown and the overall totals.</returns>
    public Task<PremiumByTypeReportDto> GetPremiumByType(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves one page of policyholders with their open claim amount weighed against their
    ///     premium, ordered by that ratio.
    /// </summary>
    /// <param name="pagination">The requested page, page size and sort. Normalized by the service.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of policyholder ratios, worst first by default.</returns>
    public Task<PagedResult<HolderClaimsRatioDto>> GetClaimsRatioPerHolder(
        PaginationQuery pagination, CancellationToken cancellationToken = default);
}

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Domain;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

/// <summary>
///     Read-only reporting queries over policyholders, policies and claims.
/// </summary>
/// <remarks>
///     Every report is a <c>GROUP BY</c> evaluated by the database: a report that read the tables into
///     memory to count them would stop scaling at exactly the point a report is most wanted. The one
///     exception is the claims ratio, which needs a roll-up from two independent directions — premium
///     per holder from the policies, claim amount per holder from the claims — and joins the two
///     roll-ups in memory; see <see cref="GetClaimsRatioPerHolder" />.
/// </remarks>
public class ReportsService(AppDbContext context) : IReportsService
{
    /// <summary>
    ///     Counts the open claims in each open status, and totals them.
    /// </summary>
    /// <remarks>
    ///     Every open status appears in the result, including one with no claims in it, so a dashboard
    ///     can chart a fixed set of bars instead of inferring the statuses that are missing. A denied
    ///     claim is not open and is therefore absent from the report entirely.
    /// </remarks>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The per-status breakdown and the overall totals.</returns>
    public async Task<OpenClaimsByStatusReportDto> GetOpenClaimsByStatus(
        CancellationToken cancellationToken = default)
    {
        // Inlined rather than calling ClaimStatusExtensions.IsOpen: the predicate sits inside an EF
        // expression, which has to translate to SQL and cannot carry a method call.
        var rows = await context.Claims
            .Where(c => c.Status == ClaimStatus.Pending || c.Status == ClaimStatus.Approved)
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, ClaimCount = g.LongCount(), TotalAmount = g.Sum(c => c.Amount) })
            .ToListAsync(cancellationToken);

        var statuses = OpenClaimStatuses()
            .Select(status =>
            {
                var row = rows.FirstOrDefault(r => r.Status == status);

                return new ClaimStatusTotalDto
                {
                    Status = status,
                    ClaimCount = row?.ClaimCount ?? 0,
                    TotalAmount = row?.TotalAmount ?? 0m,
                    AverageAmount = row is { ClaimCount: > 0 }
                        ? decimal.Round(row.TotalAmount / row.ClaimCount, 2)
                        : 0m
                };
            })
            .ToList();

        return new OpenClaimsByStatusReportDto
        {
            Statuses = statuses,
            TotalOpenClaims = statuses.Sum(s => s.ClaimCount),
            TotalOpenAmount = statuses.Sum(s => s.TotalAmount)
        };
    }

    /// <summary>
    ///     Totals the premium written in each policy type.
    /// </summary>
    /// <remarks>
    ///     Every policy type appears in the result, including one with no policies in it, for the same
    ///     reason the claims report does. Premium is counted for every policy on the books, cancelled
    ///     and expired included: the figure answers what has been written, not what is still in force.
    /// </remarks>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The per-type breakdown and the overall totals.</returns>
    public async Task<PremiumByTypeReportDto> GetPremiumByType(CancellationToken cancellationToken = default)
    {
        var rows = await context.Policies
            .GroupBy(p => p.Type)
            .Select(g => new { Type = g.Key, PolicyCount = g.LongCount(), TotalPremium = g.Sum(p => p.Premium) })
            .ToListAsync(cancellationToken);

        var types = Enum.GetValues<PolicyType>()
            .Select(type =>
            {
                var row = rows.FirstOrDefault(r => r.Type == type);

                return new PolicyTypeTotalDto
                {
                    Type = type,
                    PolicyCount = row?.PolicyCount ?? 0,
                    TotalPremium = row?.TotalPremium ?? 0m,
                    AveragePremium = row is { PolicyCount: > 0 }
                        ? decimal.Round(row.TotalPremium / row.PolicyCount, 2)
                        : 0m
                };
            })
            .ToList();

        return new PremiumByTypeReportDto
        {
            Types = types,
            TotalPolicies = types.Sum(t => t.PolicyCount),
            TotalPremium = types.Sum(t => t.TotalPremium)
        };
    }

    /// <summary>
    ///     Retrieves one page of policyholders with their open claim amount weighed against their
    ///     premium, ordered by that ratio.
    /// </summary>
    /// <remarks>
    ///     Premium per holder and claims per holder are two different roll-ups — one from the policies,
    ///     one from the claims joined back to their policy — so they are fetched as two grouped queries
    ///     and combined in memory. Only the per-holder rows are materialised, never the policies or
    ///     claims behind them, so what the report holds is proportional to the number of holders rather
    ///     than to the size of the book. The page slice is then taken from those rows.
    ///     <para>
    ///         Only holders who own at least one policy appear: the ratio of a holder with no premium
    ///         is not a figure, and a row of zeros for every such holder would bury the ones that
    ///         matter. Open claim amounts are the same amounts the coverage rules reserve, so a denied
    ///         claim does not count against its holder.
    ///     </para>
    /// </remarks>
    /// <param name="pagination">The requested page, page size and sort. Normalized before use.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of policyholder ratios, worst first by default.</returns>
    public async Task<PagedResult<HolderClaimsRatioDto>> GetClaimsRatioPerHolder(
        PaginationQuery pagination, CancellationToken cancellationToken = default)
    {
        pagination.Normalize();

        var premium = await context.Policies
            .GroupBy(p => p.PolicyHolderId)
            .Select(g => new { PolicyHolderId = g.Key, PolicyCount = g.LongCount(), TotalPremium = g.Sum(p => p.Premium) })
            .ToListAsync(cancellationToken);

        // Inlined rather than calling ClaimStatusExtensions.IsOpen: the predicate sits inside an EF
        // expression, which has to translate to SQL and cannot carry a method call.
        var claims = await (from claim in context.Claims
                            join policy in context.Policies on claim.PolicyId equals policy.Id
                            where claim.Status == ClaimStatus.Pending || claim.Status == ClaimStatus.Approved
                            group claim by policy.PolicyHolderId into g
                            select new
                            {
                                PolicyHolderId = g.Key,
                                OpenClaimCount = g.LongCount(),
                                TotalOpenClaimAmount = g.Sum(c => c.Amount)
                            })
            .ToListAsync(cancellationToken);

        var holderIds = premium.Select(p => p.PolicyHolderId).ToList();
        var names = await context.PolicyHolders
            .Where(h => holderIds.Contains(h.Id))
            .Select(h => new { h.Id, Name = h.FirstName + " " + h.LastName })
            .ToListAsync(cancellationToken);

        var rows = premium
            .Select(p =>
            {
                var holder = names.FirstOrDefault(n => n.Id == p.PolicyHolderId);
                var claim = claims.FirstOrDefault(c => c.PolicyHolderId == p.PolicyHolderId);

                return new HolderClaimsRatioDto
                {
                    PolicyHolderId = p.PolicyHolderId,
                    PolicyholderName = holder?.Name ?? string.Empty,
                    PolicyCount = p.PolicyCount,
                    TotalPremium = p.TotalPremium,
                    OpenClaimCount = claim?.OpenClaimCount ?? 0,
                    TotalOpenClaimAmount = claim?.TotalOpenClaimAmount ?? 0m,
                    ClaimsRatio = p.TotalPremium == 0m
                        ? 0m
                        : decimal.Round((claim?.TotalOpenClaimAmount ?? 0m) / p.TotalPremium, 4)
                };
            })
            .AsQueryable();

        var totalCount = rows.Count();

        var items = ApplySorting(rows, pagination.SortBy, pagination.Descending)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToList();

        return PagedResult<HolderClaimsRatioDto>.Create(items, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <summary>
    ///     The claim statuses a report considers open, in declaration order.
    /// </summary>
    /// <returns>The open claim statuses.</returns>
    private static IEnumerable<ClaimStatus> OpenClaimStatuses()
        => Enum.GetValues<ClaimStatus>().Where(status => status.IsOpen());

    /// <summary>
    ///     Orders the claims-ratio report by the requested field.
    /// </summary>
    /// <remarks>
    ///     The report exists to surface the heaviest holders first, so the default — no sort key — is
    ///     the ratio descending and <c>?descending=true</c> reverses that default, which is what
    ///     <see cref="PaginationQuery.Descending" /> promises: it reverses the order the request
    ///     established. Naming a column with <c>sortBy</c> switches to that column and takes the flag
    ///     as the direction directly, as the other list endpoints do. An unrecognised key falls back
    ///     to the default. Every branch appends the policyholder identifier as a tie-breaker, so two
    ///     holders with the same ratio keep a defined order and a page number always identifies the
    ///     same rows.
    /// </remarks>
    /// <param name="query">The policyholder rows to order.</param>
    /// <param name="sortBy">The requested field name, or null for the default.</param>
    /// <param name="descending">Whether to reverse the order the request established.</param>
    /// <returns>The ordered query.</returns>
    private static IOrderedQueryable<HolderClaimsRatioDto> ApplySorting(
        IQueryable<HolderClaimsRatioDto> query, string? sortBy, bool descending)
    {
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "totalpremium" => Order(query, descending, r => r.TotalPremium),
            "openclaimcount" => Order(query, descending, r => r.OpenClaimCount),
            "totalopenclaimamount" => Order(query, descending, r => r.TotalOpenClaimAmount),
            "policycount" => Order(query, descending, r => r.PolicyCount),
            "policyholderid" => Order(query, descending, r => r.PolicyHolderId),

            // No key, or an unrecognised one: the ratio, worst first, with ?descending=true
            // reversing it.
            _ => Order(query, !descending, r => r.ClaimsRatio)
        };
    }

    /// <summary>
    ///     Applies an ascending or descending order on one field, breaking ties on the policyholder
    ///     identifier.
    /// </summary>
    /// <param name="query">The policyholder rows to order.</param>
    /// <param name="descending">Whether to reverse the order.</param>
    /// <param name="keySelector">The field to order by.</param>
    /// <typeparam name="TKey">The type of the ordering field.</typeparam>
    /// <returns>The ordered query.</returns>
    private static IOrderedQueryable<HolderClaimsRatioDto> Order<TKey>(
        IQueryable<HolderClaimsRatioDto> query,
        bool descending,
        Expression<Func<HolderClaimsRatioDto, TKey>> keySelector)
    {
        return descending
            ? query.OrderByDescending(keySelector).ThenBy(r => r.PolicyHolderId)
            : query.OrderBy(keySelector).ThenBy(r => r.PolicyHolderId);
    }
}

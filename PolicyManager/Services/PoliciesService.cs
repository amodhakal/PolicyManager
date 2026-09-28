using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Exceptions;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

/// <summary>
///     Service implementation for managing insurance policies with transactional outbox support.
/// </summary>
public class PoliciesService(AppDbContext context) : IPoliciesService
{
    /// <summary>
    ///     Retrieves one page of policies, optionally filtered by status, ordered, with the total
    ///     count of the whole filtered result set.
    /// </summary>
    /// <remarks>
    ///     The former <c>Include</c> is gone because the projection already joins to the policyholder
    ///     to build <c>PolicyholderName</c>, and an <c>Include</c> carried into a <c>CountAsync</c> is
    ///     both redundant and a source of provider errors. Filtering, counting, ordering, skipping and
    ///     the projection all stay in the SQL the database receives.
    /// </remarks>
    /// <param name="pagination">The requested page, page size and sort. Normalized before use.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of policies matching the filter criteria.</returns>
    public async Task<PagedResult<PolicyDto>> GetAll(
        PaginationQuery pagination, PolicyStatus? status, CancellationToken cancellationToken = default)
    {
        pagination.Normalize();

        var query = context.Policies.AsQueryable();
        if (status != null) query = query.Where(p => p.Status == status);

        // Counted after the filter and before the ordering, so TotalCount describes the filtered set
        // and the database is not asked to sort rows the page will discard.
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await ApplySorting(query, pagination.SortBy, pagination.Descending)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(p => new PolicyDto
            {
                Id = p.Id,
                PolicyNumber = p.PolicyNumber,
                Premium = p.Premium,
                Status = p.Status,
                PolicyholderName = $"{p.PolicyHolder.FirstName} {p.PolicyHolder.LastName}",
                CoverageLimit = p.CoverageLimit,
                UpdatedAt = p.UpdatedAt,
                UpdatedBy = p.UpdatedBy,
                RowVersion = ConcurrencyTokens.ToToken(p.RowVersion)
            })
            .ToListAsync(cancellationToken);

        return PagedResult<PolicyDto>.Create(items, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <summary>
    ///     Orders the policy query by the requested field.
    /// </summary>
    /// <remarks>
    ///     An absent or unrecognised key falls back to the identifier, which is unique and therefore
    ///     the only ordering that is total. Every branch appends the identifier as a tie-breaker so a
    ///     page number always identifies the same rows. <c>policyholderName</c> is not offered
    ///     because it is a concatenated expression with nothing to order on when two holders share a
    ///     name; callers wanting that order should sort by <c>policyHolderId</c> instead.
    /// </remarks>
    /// <param name="query">The policies to order.</param>
    /// <param name="sortBy">The requested field name, or null for the default.</param>
    /// <param name="descending">Whether to reverse the order.</param>
    /// <returns>The ordered query.</returns>
    private static IQueryable<Policy> ApplySorting(IQueryable<Policy> query, string? sortBy, bool descending)
    {
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "policynumber" => Order(query, descending, p => p.PolicyNumber),
            "premium" => Order(query, descending, p => p.Premium),
            "status" => Order(query, descending, p => p.Status),
            "policyholderid" => Order(query, descending, p => p.PolicyHolderId),
            _ => Order(query, descending, p => p.Id)
        };
    }

    /// <summary>
    ///     Applies an ascending or descending order on one field, breaking ties on the identifier.
    /// </summary>
    /// <param name="query">The policies to order.</param>
    /// <param name="descending">Whether to reverse the order.</param>
    /// <param name="keySelector">The field to order by.</param>
    /// <typeparam name="TKey">The type of the ordering field.</typeparam>
    /// <returns>The ordered query.</returns>
    private static IOrderedQueryable<Policy> Order<TKey>(
        IQueryable<Policy> query, bool descending, Expression<Func<Policy, TKey>> keySelector)
    {
        return descending
            ? query.OrderByDescending(keySelector).ThenBy(p => p.Id)
            : query.OrderBy(keySelector).ThenBy(p => p.Id);
    }

    /// <summary>
    ///     Retrieves a policy by its unique identifier.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The policy if found; otherwise, null.</returns>
    public async Task<PolicyDto?> GetById(int id, CancellationToken cancellationToken = default)
    {
        return await context.Policies
            .Include(p => p.PolicyHolder)
            .Where(p => p.Id == id)
            .Select(p => new PolicyDto
            {
                Id = p.Id,
                PolicyNumber = p.PolicyNumber,
                Premium = p.Premium,
                Status = p.Status,
                PolicyholderName = $"{p.PolicyHolder.FirstName} {p.PolicyHolder.LastName}",
                CoverageLimit = p.CoverageLimit,
                UpdatedAt = p.UpdatedAt,
                UpdatedBy = p.UpdatedBy,
                RowVersion = ConcurrencyTokens.ToToken(p.RowVersion)
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    ///     Creates a new policy and records an outbox message transactionally.
    /// </summary>
    /// <remarks>
    ///     The holder is checked first so a dangling reference is a 404 naming the missing
    ///     holder, rather than a foreign key violation surfacing as a 409 or a 500. The
    ///     database constraint stays as the backstop for races between the check and the insert.
    /// </remarks>
    /// <param name="dto">The policy data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created policy.</returns>
    /// <exception cref="NotFoundException">The referenced policyholder does not exist.</exception>
    public async Task<int> Create(CreatePolicyDto dto, CancellationToken cancellationToken = default)
    {
        var holderExists = await context.PolicyHolders
            .AnyAsync(h => h.Id == dto.PolicyHolderId, cancellationToken);
        if (!holderExists) throw new NotFoundException("PolicyHolder", dto.PolicyHolderId);

        var policy = new Policy
        {
            Premium = dto.Premium,
            Status = PolicyStatus.Active,
            PolicyHolderId = dto.PolicyHolderId,
            Type = dto.Type ?? throw new InvalidOperationException(
                $"{nameof(CreatePolicyDto.Type)} is required and was not supplied."),
            CoverageLimit = dto.CoverageLimit,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate
        };

        await context.AddWithOutboxAsync(
            policy,
            "PolicyCreated",
            p => new { p.Id, p.PolicyNumber, p.Premium, p.PolicyHolderId, p.Status },
            cancellationToken);

        return policy.Id;
    }

    /// <summary>
    ///     Updates an existing policy.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="dto">The policy data transfer object containing updated details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="NotFoundException">The policy does not exist.</exception>
    public async Task Update(int id, UpdatePolicyDto dto, CancellationToken cancellationToken = default)
    {
        var policy = await context.Policies.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException("Policy", id);

        // Applied conditionally: a field the caller omitted must leave the stored value alone rather
        // than overwrite it with a type default.
        if (dto.Status is not null) policy.Status = dto.Status.Value;
        if (dto.Premium is not null) policy.Premium = dto.Premium.Value;

        context.ApplyOriginalValue(policy, dto.RowVersion);

        await context.SaveWithOutboxAsync(
            policy,
            "PolicyUpdated",
            p => new { p.Id, p.PolicyNumber, p.Premium, p.Status },
            cancellationToken);
    }

    /// <summary>
    ///     Cancels an existing policy by setting its status to Cancelled and recording an outbox message.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="rowVersion">
    ///     The concurrency token the caller read, or null to cancel unconditionally.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="NotFoundException">The policy does not exist.</exception>
    public async Task Cancel(
        int id,
        string? rowVersion = null,
        CancellationToken cancellationToken = default)
    {
        var policy = await context.Policies.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException("Policy", id);

        policy.Status = PolicyStatus.Cancelled;

        context.ApplyOriginalValue(policy, rowVersion);

        await context.SaveWithOutboxAsync(
            policy,
            "PolicyCancelled",
            p => new { p.Id, p.PolicyNumber, p.Status },
            cancellationToken);
    }
}

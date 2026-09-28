using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

/// <summary>
///     Service implementation for managing insurance claims with transactional outbox support.
/// </summary>
public class ClaimsService(AppDbContext context) : IClaimsService
{
    /// <summary>
    ///     Retrieves one page of claims, ordered, with the total count of the whole result set.
    /// </summary>
    /// <param name="pagination">The requested page, page size and sort. Normalized before use.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of claims as ClaimDto objects.</returns>
    public async Task<PagedResult<ClaimDto>> GetAll(
        PaginationQuery pagination, CancellationToken cancellationToken = default)
    {
        pagination.Normalize();

        var totalCount = await context.Claims.CountAsync(cancellationToken);

        // The sort and the offset are applied to the entity query and the projection follows them, so
        // the database does the paging and only the requested rows come back.
        var items = await ApplySorting(context.Claims, pagination.SortBy, pagination.Descending)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(c => new ClaimDto
            {
                Id = c.Id,
                ClaimNumber = c.ClaimNumber,
                PolicyId = c.PolicyId,
                Amount = c.Amount,
                Description = c.Description,
                Status = c.Status,
                FiledAt = c.FiledAt
            })
            .ToListAsync(cancellationToken);

        return PagedResult<ClaimDto>.Create(items, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <summary>
    ///     Orders the claim query by the requested field.
    /// </summary>
    /// <remarks>
    ///     An absent or unrecognised key falls back to the identifier, which is unique and therefore
    ///     the only ordering that is total. Every branch appends the identifier as a tie-breaker, so
    ///     rows with the same amount or status keep a defined order and a page number always
    ///     identifies the same rows.
    /// </remarks>
    /// <param name="query">The claims to order.</param>
    /// <param name="sortBy">The requested field name, or null for the default.</param>
    /// <param name="descending">Whether to reverse the order.</param>
    /// <returns>The ordered query.</returns>
    private static IQueryable<Claim> ApplySorting(IQueryable<Claim> query, string? sortBy, bool descending)
    {
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "claimnumber" => Order(query, descending, c => c.ClaimNumber),
            "amount" => Order(query, descending, c => c.Amount),
            "status" => Order(query, descending, c => c.Status),
            "filedat" => Order(query, descending, c => c.FiledAt),
            "policyid" => Order(query, descending, c => c.PolicyId),
            _ => Order(query, descending, c => c.Id)
        };
    }

    /// <summary>
    ///     Applies an ascending or descending order on one field, breaking ties on the identifier.
    /// </summary>
    /// <param name="query">The claims to order.</param>
    /// <param name="descending">Whether to reverse the order.</param>
    /// <param name="keySelector">The field to order by.</param>
    /// <typeparam name="TKey">The type of the ordering field.</typeparam>
    /// <returns>The ordered query.</returns>
    private static IOrderedQueryable<Claim> Order<TKey>(
        IQueryable<Claim> query, bool descending, Expression<Func<Claim, TKey>> keySelector)
    {
        return descending
            ? query.OrderByDescending(keySelector).ThenBy(c => c.Id)
            : query.OrderBy(keySelector).ThenBy(c => c.Id);
    }

    /// <summary>
    ///     Retrieves a claim by its unique identifier.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The claim if found; otherwise, null.</returns>
    public async Task<ClaimDto?> GetById(int id, CancellationToken cancellationToken = default)
    {
        return await context.Claims
            .Where(c => c.Id == id)
            .Select(c => new ClaimDto
            {
                Id = c.Id,
                ClaimNumber = c.ClaimNumber,
                PolicyId = c.PolicyId,
                Amount = c.Amount,
                Description = c.Description,
                Status = c.Status,
                FiledAt = c.FiledAt
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    ///     Creates a new claim and records an outbox message transactionally.
    /// </summary>
    /// <param name="dto">The claim data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created claim.</returns>
    public async Task<int> Create(CreateClaimDto dto, CancellationToken cancellationToken = default)
    {
        var claim = new Claim
        {
            PolicyId = dto.PolicyId,
            Amount = dto.Amount,
            Description = dto.Description,
            Status = ClaimStatus.Pending,
            FiledAt = DateTime.UtcNow
        };

        await context.AddWithOutboxAsync(
            claim,
            "ClaimCreated",
            c => new { c.Id, c.PolicyId, c.Amount, c.Status, c.FiledAt },
            cancellationToken);

        return claim.Id;
    }

    /// <summary>
    ///     Updates the status of an existing claim and records an outbox message.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="dto">The claim status update data transfer object containing the new status.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task UpdateStatus(int id, UpdateClaimStatusDto dto, CancellationToken cancellationToken = default)
    {
        var claim = await context.Claims.FindAsync([id], cancellationToken);
        if (claim == null) return;

        claim.Status = dto.Status ?? throw new InvalidOperationException(
            $"{nameof(UpdateClaimStatusDto.Status)} is required and was not supplied.");

        await context.SaveWithOutboxAsync(
            claim,
            "ClaimStatusUpdated",
            c => new { c.Id, c.PolicyId, c.Status },
            cancellationToken);
    }
}

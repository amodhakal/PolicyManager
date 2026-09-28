using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;

namespace PolicyManager.Services;

/// <summary>
///     Service implementation for managing policyholders with caching and transactional outbox support.
/// </summary>
public class PolicyHoldersService(AppDbContext context, IMemoryCache cache) : IPolicyHoldersService
{
    /// <summary>
    ///     Approximate managed-memory footprint, in bytes, of one <see cref="PolicyHolderDto" />: a 16-byte object
    ///     header, the identifier and its padding, three object references to the strings, and the UTF-16 character
    ///     data of a typical first name, last name and email address. Rounded up to leave headroom for longer values.
    /// </summary>
    private const long PolicyHolderSizeBytes = 256;

    /// <summary>
    ///     How long a cached single policyholder stays valid before it is re-read. Deliberately short: an indexed
    ///     seek is cheap to repeat, and a stale record is visible to only the one reader who requested that
    ///     identifier.
    /// </summary>
    private static readonly TimeSpan SinglePolicyHolderExpiration = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Retrieves one page of policyholders, ordered, with the total count of the whole result set.
    /// </summary>
    /// <remarks>
    ///     The full-list cache that used to sit on this path is retired. It cached the entire table, so a
    ///     paginated read served out of it would still have had to hold every row in memory — the exact
    ///     unbounded growth the 10 MiB <c>SizeLimit</c> exists to prevent, and a table large enough to
    ///     overflow that limit evicted the entry and turned every page into a full rebuild. A single
    ///     unkeyed-by-page entry also cannot serve more than one sort order, and the caller still needs
    ///     <c>TotalCount</c> and the page slice, both of which the cache could not supply without the
    ///     full materialization it existed to avoid. Letting the database do <c>ORDER BY</c> /
    ///     <c>OFFSET</c>/<c>FETCH</c> returns a bounded, index-backed read instead.
    /// </remarks>
    /// <param name="pagination">The requested page, page size and sort. Normalized before use.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of policyholders. Not served from the cache.</returns>
    public async Task<PagedResult<PolicyHolderDto>> GetAll(
        PaginationQuery pagination, CancellationToken cancellationToken = default)
    {
        pagination.Normalize();

        // Counted on the un-ordered, un-paged set: the count is the same either way, and letting the
        // database pick the plan for a bare COUNT is cheaper than ordering rows it will then discard.
        var totalCount = await context.PolicyHolders.CountAsync(cancellationToken);

        // Ordering happens on the entity query and the projection to the DTO happens after Skip/Take,
        // so the database applies the sort and the offset rather than materializing every holder to
        // slice in memory.
        var items = await ApplySorting(context.PolicyHolders, pagination.SortBy, pagination.Descending)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(h => new PolicyHolderDto
            {
                Id = h.Id,
                FirstName = h.FirstName,
                LastName = h.LastName,
                Email = h.Email
            })
            .ToListAsync(cancellationToken);

        return PagedResult<PolicyHolderDto>.Create(items, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <summary>
    ///     Orders the policyholder query by the requested field.
    /// </summary>
    /// <remarks>
    ///     An absent or unrecognised key falls back to the identifier, which is unique and therefore
    ///     the only ordering that is total. Every branch appends the identifier as a tie-breaker, so
    ///     rows with the same name or email still have a defined order — without that, the same page
    ///     number could return different rows on two consecutive requests.
    /// </remarks>
    /// <param name="query">The policyholders to order.</param>
    /// <param name="sortBy">The requested field name, or null for the default.</param>
    /// <param name="descending">Whether to reverse the order.</param>
    /// <returns>The ordered query.</returns>
    private static IQueryable<PolicyHolder> ApplySorting(
        IQueryable<PolicyHolder> query, string? sortBy, bool descending)
    {
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "firstname" => Order(query, descending, h => h.FirstName),
            "lastname" => Order(query, descending, h => h.LastName),
            "email" => Order(query, descending, h => h.Email),
            _ => Order(query, descending, h => h.Id)
        };
    }

    /// <summary>
    ///     Applies an ascending or descending order on one field, breaking ties on the identifier.
    /// </summary>
    /// <param name="query">The policyholders to order.</param>
    /// <param name="descending">Whether to reverse the order.</param>
    /// <param name="keySelector">The field to order by.</param>
    /// <typeparam name="TKey">The type of the ordering field.</typeparam>
    /// <returns>The ordered query.</returns>
    private static IOrderedQueryable<PolicyHolder> Order<TKey>(
        IQueryable<PolicyHolder> query, bool descending, Expression<Func<PolicyHolder, TKey>> keySelector)
    {
        return descending
            ? query.OrderByDescending(keySelector).ThenBy(h => h.Id)
            : query.OrderBy(keySelector).ThenBy(h => h.Id);
    }

    /// <summary>
    ///     Retrieves a policyholder by their unique identifier.
    /// </summary>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The policyholder if found; otherwise, null.</returns>
    public async Task<PolicyHolderDto?> GetById(int id, CancellationToken cancellationToken = default)
    {
        var key = CacheKeys.ById(id);
        if (cache.TryGetValue(key, out PolicyHolderDto? cached)) return cached;

        var holder = await context.PolicyHolders.Where(p => p.Id == id)
            .Select(p => new PolicyHolderDto
                { Id = p.Id, FirstName = p.FirstName, LastName = p.LastName, Email = p.Email })
            .FirstOrDefaultAsync(cancellationToken);

        if (holder != null) cache.Set(key, holder, new MemoryCacheEntryOptions
        {
            Size = PolicyHolderSizeBytes,
            Priority = CacheItemPriority.High,
            AbsoluteExpirationRelativeToNow = SinglePolicyHolderExpiration
        });

        return holder;
    }

    /// <summary>
    ///     Creates a new policyholder and records an outbox message transactionally.
    /// </summary>
    /// <remarks>
    ///     Only the single-holder key is invalidated. There is no list entry to evict any more, so a
    ///     write from outside this service can no longer be masked by a stale collection.
    /// </remarks>
    /// <param name="dto">The policyholder data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created policyholder.</returns>
    public async Task<int> Create(CreatePolicyHolderDto dto, CancellationToken cancellationToken = default)
    {
        var holder = new PolicyHolder
            { FirstName = dto.FirstName, LastName = dto.LastName, Email = dto.Email };

        await context.AddWithOutboxAsync(
            holder,
            "PolicyHolderCreated",
            h => new { h.Id, h.FirstName, h.LastName, h.Email },
            cancellationToken);

        cache.Remove(CacheKeys.ById(holder.Id));
        return holder.Id;
    }
}

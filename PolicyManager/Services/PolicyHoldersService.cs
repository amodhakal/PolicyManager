using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Exceptions;
using PolicyManager.Models;

namespace PolicyManager.Services;

/// <summary>
///     Service implementation for managing policyholders with caching and transactional outbox support.
/// </summary>
public class PolicyHoldersService(
    AppDbContext context,
    IMemoryCache cache,
    PolicyHolderWriteGenerations generations,
    IPiiGuard pii) : IPolicyHoldersService
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

        var mayDisclose = pii.MayDisclose();

        // Decryption happens in the value converter, on the way out of the database, so a projection
        // cannot accidentally bypass it. Masking happens after that, here, and only for callers not
        // entitled to the address.
        var items = await ApplySorting(context.PolicyHolders, pagination.SortBy, pagination.Descending)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(h => new PolicyHolderDto
            {
                Id = h.Id,
                FirstName = h.FirstName,
                LastName = h.LastName,
                Email = h.Email,
                UpdatedAt = h.UpdatedAt,
                UpdatedBy = h.UpdatedBy,
                RowVersion = ConcurrencyTokens.ToToken(h.RowVersion)
            })
            .ToListAsync(cancellationToken);

        foreach (var holder in items)
        {
            if (!mayDisclose) holder.Email = PiiGuard.Mask(holder.Email);

            // Audited per holder rather than once per page: the log has to say whose data was read,
            // and a page-level row cannot say that.
            await pii.RecordAccessAsync(holder.Id, mayDisclose, cancellationToken);
        }

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
    /// <remarks>
    ///     The generation is read once, before the database is consulted, and the key it produces is
    ///     the only one written to. A write that commits while this read is in flight advances the
    ///     generation, so the value this read goes on to cache lands under a key nothing will ask for
    ///     again. Reading it again afterwards to build the key would reopen the race: the key would
    ///     then name a generation whose commit the reader had never seen.
    /// </remarks>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The policyholder if found; otherwise, null.</returns>
    public async Task<PolicyHolderDto?> GetById(int id, CancellationToken cancellationToken = default)
    {
        var key = CacheKeys.ById(id, generations.Current(id));
        if (cache.TryGetValue(key, out PolicyHolderDto? cached)) return cached;

        var holder = await context.PolicyHolders.Where(p => p.Id == id)
            .Select(p => new PolicyHolderDto
            {
                Id = p.Id,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Email = p.Email,
                UpdatedAt = p.UpdatedAt,
                UpdatedBy = p.UpdatedBy,
                RowVersion = ConcurrencyTokens.ToToken(p.RowVersion)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (holder != null)
        {
            // Masking happens after the cache, and the cached copy keeps whatever the first caller
            // was entitled to. A shared cache entry holding a disclosed address would then be served
            // to an agent who is only entitled to the masked form, so the mask is applied per
            // response and never to the cached value.
            var mayDisclose = pii.MayDisclose();
            await pii.RecordAccessAsync(holder.Id, mayDisclose, cancellationToken);

            cache.Set(key, holder, new MemoryCacheEntryOptions
            {
                Size = PolicyHolderSizeBytes,
                Priority = CacheItemPriority.High,
                AbsoluteExpirationRelativeToNow = SinglePolicyHolderExpiration
            });

            return mayDisclose ? holder : Masked(holder);
        }

        return null;
    }

    /// <summary>
    ///     Returns a copy of a holder with the contact address masked.
    /// </summary>
    /// <param name="holder">The holder read from the store.</param>
    /// <returns>A copy safe to return to a caller not entitled to the address.</returns>
    private static PolicyHolderDto Masked(PolicyHolderDto holder) => new()
    {
        Id = holder.Id,
        FirstName = holder.FirstName,
        LastName = holder.LastName,
        Email = PiiGuard.Mask(holder.Email),
        UpdatedAt = holder.UpdatedAt,
        UpdatedBy = holder.UpdatedBy,
        RowVersion = holder.RowVersion
    };

    /// <summary>
    ///     Creates a new policyholder and records an outbox message transactionally.
    /// </summary>
    /// <remarks>
    ///     The email is checked first so a duplicate is a 409 naming the address on every provider,
    ///     including the in-memory one, which enforces no unique index. The unique index stays as the
    ///     backstop for races between the check and the insert, and the handler maps its violation to
    ///     the same 409.
    ///     <para>
    ///     Nothing is cached here, so nothing needs evicting: the row is new, and the identity a
    ///     cached read would have looked up has no cached entry to be stale. The generation is still
    ///     advanced so that an in-flight read for the identifier - possible once an identity has been
    ///     reused, or against a row deleted and recreated - cannot leave an entry that a later read
    ///     of this id would treat as its own.
    ///     </para>
    /// </remarks>
    /// <param name="dto">The policyholder data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created policyholder.</returns>
    /// <exception cref="ConflictException">A policyholder with the same email already exists.</exception>
    public async Task<int> Create(CreatePolicyHolderDto dto, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = dto.Email.ToLowerInvariant();
        var emailTaken = await context.PolicyHolders
            .AnyAsync(h => h.Email.ToLower() == normalizedEmail, cancellationToken);
        if (emailTaken) throw new ConflictException($"A policyholder with email '{dto.Email}' already exists.");

        var holder = new PolicyHolder
            { FirstName = dto.FirstName, LastName = dto.LastName, Email = dto.Email };

        // Checked here as well as by the unique index, because the index on the blind index is not
        // applied until the second-phase migration and the index on the address column is over
        // randomised ciphertext and can never fire. Without this the duplicate-address 409 would
        // silently disappear for the length of the rollout.
        var duplicate = await context.PolicyHolders
            .AsNoTracking()
            .AnyAsync(h => h.EmailHash == PiiCipher.Current!.BlindIndex(dto.Email), cancellationToken);

        if (duplicate) throw new ConflictException(
            $"A policyholder with the email address '{PiiGuard.Mask(dto.Email)}' already exists.");

        // The address is deliberately absent. An outbox message is a broadcast to whatever consumes
        // it, and its retention is not this service's to bound; putting a policyholder's email there
        // would copy personal data out of the one store that protects it. Consumers that need it can
        // read the holder, which is access-controlled and audited.
        await context.AddWithOutboxAsync(
            holder,
            "PolicyHolderCreated",
            h => new { h.Id, h.FirstName, h.LastName, HasEmail = h.Email.Length > 0 },
            cancellationToken);

        generations.Advance(holder.Id);
        return holder.Id;
    }

    /// <summary>
    ///     Updates an existing policyholder, applying only the fields the caller supplied.
    /// </summary>
    /// <remarks>
    ///     The generation is advanced after the transaction commits, not before. Before the commit the
    ///     database still answers with the old row, so a reader that took the new generation would
    ///     cache that old row under a key that stays live - the staleness this ordering exists to
    ///     prevent. After the commit, every reader on the new generation reads the new row.
    /// </remarks>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="dto">The fields to change. A null field is left as it is.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="NotFoundException">The policyholder does not exist.</exception>
    /// <exception cref="ConflictException">Another policyholder already holds the supplied email.</exception>
    public async Task Update(
        int id, UpdatePolicyHolderDto dto, CancellationToken cancellationToken = default)
    {
        var holder = await context.PolicyHolders.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException("PolicyHolder", id);

        if (dto.Email is not null && !string.Equals(dto.Email, holder.Email, StringComparison.OrdinalIgnoreCase))
        {
            var normalizedEmail = dto.Email.ToLowerInvariant();
            var emailTaken = await context.PolicyHolders
                .AnyAsync(h => h.Id != id && h.Email.ToLower() == normalizedEmail, cancellationToken);
            if (emailTaken) throw new ConflictException($"A policyholder with email '{dto.Email}' already exists.");
        }

        if (dto.FirstName is not null) holder.FirstName = dto.FirstName;
        if (dto.LastName is not null) holder.LastName = dto.LastName;
        if (dto.Email is not null) holder.Email = dto.Email;

        await context.SaveWithOutboxAsync(
            holder,
            "PolicyHolderUpdated",
            h => new { h.Id, h.FirstName, h.LastName, h.Email },
            cancellationToken);

        generations.Advance(id);
    }

    /// <summary>
    ///     Deletes a policyholder who owns no policies, and records an outbox message transactionally.
    /// </summary>
    /// <remarks>
    ///     Policies are refused rather than removed. A claim is a financial record carrying the
    ///     adjudication trail, and the foreign keys restrict for exactly that reason: a holder with
    ///     claims could never be deleted without destroying them. Rejecting a holder who merely has
    ///     policies is the same rule one step earlier - it keeps the caller from discovering it as an
    ///     opaque constraint violation, and it gives them the choice the API already offers for a
    ///     policy, which is to cancel it.
    /// </remarks>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="NotFoundException">The policyholder does not exist.</exception>
    /// <exception cref="ConflictException">The policyholder still owns policies.</exception>
    public async Task Delete(int id, CancellationToken cancellationToken = default)
    {
        var holder = await context.PolicyHolders.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException("PolicyHolder", id);

        var policyCount = await context.Policies.CountAsync(p => p.PolicyHolderId == id, cancellationToken);
        if (policyCount > 0)
        {
            throw new ConflictException(
                $"Policyholder '{id}' still owns {policyCount} policy/policies. Cancel them before deleting the holder.");
        }

        context.Remove(holder);

        await context.SaveWithOutboxAsync(
            holder,
            "PolicyHolderDeleted",
            h => new { h.Id, h.FirstName, h.LastName, h.Email },
            cancellationToken);

        generations.Advance(id);
    }
}

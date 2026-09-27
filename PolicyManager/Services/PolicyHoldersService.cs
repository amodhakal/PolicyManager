using System.Text.Json;
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
    ///     Approximate managed-memory footprint, in bytes, of the list instance and its backing array that carry a
    ///     cached collection, excluding the per-holder cost already accounted for by
    ///     <see cref="PolicyHolderSizeBytes" />.
    /// </summary>
    private const long CollectionOverheadBytes = 64;

    /// <summary>
    ///     How long a cached collection of all policyholders stays valid before it is re-materialized. Deliberately
    ///     shorter than <see cref="SinglePolicyHolderExpiration" />: rebuilding the collection costs a full table scan
    ///     whereas rebuilding one entry costs an indexed seek, so a shorter lifetime still amortizes far better while
    ///     bounding how long a write that bypasses service-level invalidation can stay invisible to list readers.
    /// </summary>
    private static readonly TimeSpan AllPolicyHoldersExpiration = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     How long a cached single policyholder stays valid before it is re-read. Longer than
    ///     <see cref="AllPolicyHoldersExpiration" /> because an indexed seek is cheap to repeat and a stale record is
    ///     visible to only the one reader who requested that identifier.
    /// </summary>
    private static readonly TimeSpan SinglePolicyHolderExpiration = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Retrieves all policyholders from the database.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A list of all policyholders.</returns>
    public async Task<IEnumerable<PolicyHolderDto>> GetAll(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKeys.AllPolicyHolders, out IEnumerable<PolicyHolderDto>? cached)) return cached!;

        var holders = await context.PolicyHolders.Select(p => new PolicyHolderDto
        {
            Id = p.Id,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email
        }).ToListAsync(cancellationToken);

        cache.Set(CacheKeys.AllPolicyHolders, holders, new MemoryCacheEntryOptions
        {
            Size = CollectionOverheadBytes + holders.Count * PolicyHolderSizeBytes,
            Priority = CacheItemPriority.Low,
            AbsoluteExpirationRelativeToNow = AllPolicyHoldersExpiration
        });

        return holders;
    }

    /// <summary>
    ///     Retrieves a policyholder by their unique identifier.
    /// </summary>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The policyholder if found; otherwise, null.</returns>
    public async Task<PolicyHolderDto?> GetById(int id, CancellationToken cancellationToken = default)
    {
        var key = $"policyholders:{id}";
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
    /// <param name="dto">The policyholder data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created policyholder.</returns>
    public async Task<int> Create(CreatePolicyHolderDto dto, CancellationToken cancellationToken = default)
    {
        var holder = new PolicyHolder
            { FirstName = dto.FirstName, LastName = dto.LastName, Email = dto.Email };

        context.PolicyHolders.Add(holder);

        var outboxMessage = new OutboxMessage
        {
            Type = "PolicyHolderCreated",
            Content = JsonSerializer.Serialize(new { holder.Id, holder.FirstName, holder.LastName, holder.Email })
        };
        context.OutboxMessages.Add(outboxMessage);

        await context.SaveChangesAsync(cancellationToken);

        cache.Remove("policyholders:all");
        cache.Remove($"policyholders:{holder.Id}");
        return holder.Id;
    }
}

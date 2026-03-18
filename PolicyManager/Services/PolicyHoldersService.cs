using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;

namespace PolicyManager.Services;

/// <summary>
///     Service implementation for managing policyholders with caching support.
/// </summary>
public class PolicyHoldersService(AppDbContext context, IMemoryCache cache) : IPolicyHoldersService
{
    /// <summary>
    ///     Retrieves all policyholders from the database.
    /// </summary>
    /// <returns>A list of all policyholders.</returns>
    public async Task<IEnumerable<PolicyHolderDto>> GetAll()
    {
        if (cache.TryGetValue("policyholders:all", out IEnumerable<PolicyHolderDto>? cached)) return cached!;

        var holders = await context.PolicyHolders.Select(p => new PolicyHolderDto
        {
            Id = p.Id,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email
        }).ToListAsync();

        cache.Set("policyholders:all", holders, TimeSpan.FromMinutes(5));
        return holders;
    }

    /// <summary>
    ///     Retrieves a policyholder by their unique identifier.
    /// </summary>
    /// <param name="id">The policyholder identifier.</param>
    /// <returns>The policyholder if found; otherwise, null.</returns>
    public async Task<PolicyHolderDto?> GetById(int id)
    {
        var key = $"policyholders:{id}";
        if (cache.TryGetValue(key, out PolicyHolderDto? cached)) return cached;

        var holder = await context.PolicyHolders.Where(p => p.Id == id)
            .Select(p => new PolicyHolderDto
                { Id = p.Id, FirstName = p.FirstName, LastName = p.LastName, Email = p.Email })
            .FirstOrDefaultAsync();

        if (holder != null) cache.Set(key, holder, TimeSpan.FromMinutes(5));

        return holder;
    }

    /// <summary>
    ///     Creates a new policyholder.
    /// </summary>
    /// <param name="dto">The policyholder data transfer object.</param>
    /// <returns>The unique identifier of the newly created policyholder.</returns>
    public async Task<int> Create(CreatePolicyHolderDto dto)
    {
        var holder = new PolicyHolder
            { FirstName = dto.FirstName, LastName = dto.LastName, Email = dto.Email };

        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        cache.Remove("policyholders:all");
        return holder.Id;
    }
}
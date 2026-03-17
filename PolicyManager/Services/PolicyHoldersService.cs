using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;

namespace PolicyManager.Services;

public class PolicyHoldersService(AppDbContext context, IMemoryCache cache) : IPolicyHoldersService
{
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
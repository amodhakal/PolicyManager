using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;

namespace PolicyManager.Services;

public class PolicyHoldersService(AppDbContext context) : IPolicyHoldersService
{
    public async Task<IEnumerable<PolicyHolderDto>> GetAll()
    {
        return await context.PolicyHolders.Select(p => new PolicyHolderDto
        {
            Id = p.Id,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email
        }).ToListAsync();
    }

    public async Task<PolicyHolderDto?> GetById(int id)
    {
        return await context.PolicyHolders.Where(p => p.Id == id)
            .Select(p => new PolicyHolderDto
                { Id = p.Id, FirstName = p.FirstName, LastName = p.LastName, Email = p.Email }).FirstOrDefaultAsync();
    }

    public async Task<int> Create(CreatePolicyHolderDto dto)
    {
        var holder = new PolicyHolder
            { FirstName = dto.FirstName, LastName = dto.LastName, Email = dto.Email };

        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();
        return holder.Id;
    }
}
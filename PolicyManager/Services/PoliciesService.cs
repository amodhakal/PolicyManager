using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

public class PoliciesService(AppDbContext context) : IPoliciesService
{
    public async Task<IEnumerable<PolicyDto>> GetAll(PolicyStatus? status)
    {
        var query = context.Policies.Include(p => p.PolicyHolder).AsQueryable();
        if (status != null) query = query.Where(p => p.Status == status);

        return await query.Select(p => new PolicyDto
        {
            Id = p.Id,
            PolicyNumber = p.PolicyNumber,
            Premium = p.Premium,
            Status = p.Status,
            PolicyholderName = $"{p.PolicyHolder.FirstName} {p.PolicyHolder.LastName}"
        }).ToListAsync();
    }

    public async Task<PolicyDto?> GetById(int id)
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
                PolicyholderName = $"{p.PolicyHolder.FirstName} {p.PolicyHolder.LastName}"
            })
            .FirstOrDefaultAsync();
    }

    public async Task<int> Create(CreatePolicyDto dto)
    {
        var policy = new Policy
        {
            Premium = dto.Premium,
            Status = PolicyStatus.Active,
            PolicyHolderId = dto.PolicyHolderId
        };

        context.Policies.Add(policy);
        await context.SaveChangesAsync();
        return policy.Id;
    }

    public async Task Update(int id, UpdatePolicyDto dto)
    {
        var policy = await context.Policies.FindAsync(id);
        if (policy == null) return;

        policy.Status = dto.Status;
        policy.Premium = dto.Premium;
        await context.SaveChangesAsync();
    }

    public async Task Cancel(int id)
    {
        var policy = await context.Policies.FindAsync(id);
        if (policy == null) return;

        policy.Status = PolicyStatus.Cancelled;
        await context.SaveChangesAsync();
    }
}
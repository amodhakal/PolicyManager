using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

public class ClaimsService(AppDbContext context) : IClaimsService
{
    public async Task<IEnumerable<ClaimDto>> GetAll()
    {
        return await context.Claims
            .Select(c => new ClaimDto
            {
                Id = c.Id,
                PolicyId = c.PolicyId,
                Amount = c.Amount,
                Status = c.Status,
                FiledAt = c.FiledAt
            }).ToListAsync();
    }

    public async Task<ClaimDto?> GetById(int id)
    {
        return await context.Claims
            .Where(c => c.Id == id)
            .Select(c => new ClaimDto
            {
                Id = c.Id,
                PolicyId = c.PolicyId,
                Amount = c.Amount,
                Status = c.Status,
                FiledAt = c.FiledAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<int> Create(ClaimDto dto)
    {
        var claim = new Claim
        {
            PolicyId = dto.PolicyId,
            Amount = dto.Amount,
            Status = ClaimStatus.Pending,
            FiledAt = DateTime.UtcNow
        };

        context.Claims.Add(claim);
        await context.SaveChangesAsync();
        return claim.Id;
    }

    public async Task UpdateStatus(int id, ClaimDto dto)
    {
        var claim = await context.Claims.FindAsync(id);
        if (claim == null) return;

        claim.Status = dto.Status;
        await context.SaveChangesAsync();
    }
}
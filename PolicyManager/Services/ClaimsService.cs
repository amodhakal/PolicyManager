using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

/// <summary>
///     Service implementation for managing insurance claims.
/// </summary>
public class ClaimsService(AppDbContext context) : IClaimsService
{
    /// <summary>
    ///     Retrieves all claims from the database.
    /// </summary>
    /// <returns>A list of all claims as ClaimDto objects.</returns>
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

    /// <summary>
    ///     Retrieves a claim by its unique identifier.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <returns>The claim if found; otherwise, null.</returns>
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

    /// <summary>
    ///     Creates a new claim.
    /// </summary>
    /// <param name="dto">The claim data transfer object.</param>
    /// <returns>The unique identifier of the newly created claim.</returns>
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

    /// <summary>
    ///     Updates the status of an existing claim.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="dto">The claim data containing the new status.</param>
    public async Task UpdateStatus(int id, ClaimDto dto)
    {
        var claim = await context.Claims.FindAsync(id);
        if (claim == null) return;

        claim.Status = dto.Status;
        await context.SaveChangesAsync();
    }
}
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

/// <summary>
///     Service implementation for managing insurance policies.
/// </summary>
public class PoliciesService(AppDbContext context) : IPoliciesService
{
    /// <summary>
    ///     Retrieves all policies, optionally filtered by status.
    /// </summary>
    /// <param name="status">Optional status filter.</param>
    /// <returns>A list of policies matching the filter criteria.</returns>
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

    /// <summary>
    ///     Retrieves a policy by its unique identifier.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <returns>The policy if found; otherwise, null.</returns>
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

    /// <summary>
    ///     Creates a new policy.
    /// </summary>
    /// <param name="dto">The policy data transfer object.</param>
    /// <returns>The unique identifier of the newly created policy.</returns>
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

    /// <summary>
    ///     Updates an existing policy.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="dto">The policy data transfer object containing updated details.</param>
    public async Task Update(int id, UpdatePolicyDto dto)
    {
        var policy = await context.Policies.FindAsync(id);
        if (policy == null) return;

        policy.Status = dto.Status;
        policy.Premium = dto.Premium;
        await context.SaveChangesAsync();
    }

    /// <summary>
    ///     Cancels an existing policy by setting its status to Cancel.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    public async Task Cancel(int id)
    {
        var policy = await context.Policies.FindAsync(id);
        if (policy == null) return;

        policy.Status = PolicyStatus.Cancelled;
        await context.SaveChangesAsync();
    }
}
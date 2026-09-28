using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

/// <summary>
///     Service implementation for managing insurance claims with transactional outbox support.
/// </summary>
public class ClaimsService(AppDbContext context) : IClaimsService
{
    /// <summary>
    ///     Retrieves all claims from the database.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A list of all claims as ClaimDto objects.</returns>
    public async Task<IEnumerable<ClaimDto>> GetAll(CancellationToken cancellationToken = default)
    {
        return await context.Claims
            .Select(c => new ClaimDto
            {
                Id = c.Id,
                ClaimNumber = c.ClaimNumber,
                PolicyId = c.PolicyId,
                Amount = c.Amount,
                Description = c.Description,
                Status = c.Status,
                FiledAt = c.FiledAt
            }).ToListAsync(cancellationToken);
    }

    /// <summary>
    ///     Retrieves a claim by its unique identifier.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The claim if found; otherwise, null.</returns>
    public async Task<ClaimDto?> GetById(int id, CancellationToken cancellationToken = default)
    {
        return await context.Claims
            .Where(c => c.Id == id)
            .Select(c => new ClaimDto
            {
                Id = c.Id,
                ClaimNumber = c.ClaimNumber,
                PolicyId = c.PolicyId,
                Amount = c.Amount,
                Description = c.Description,
                Status = c.Status,
                FiledAt = c.FiledAt
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    ///     Creates a new claim and records an outbox message transactionally.
    /// </summary>
    /// <param name="dto">The claim creation data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created claim.</returns>
    public async Task<int> Create(CreateClaimDto dto, CancellationToken cancellationToken = default)
    {
        var claim = new Claim
        {
            PolicyId = dto.PolicyId,
            Amount = dto.Amount,
            Description = dto.Description,
            Status = ClaimStatus.Pending,
            FiledAt = DateTime.UtcNow
        };

        context.Claims.Add(claim);

        var outboxMessage = new OutboxMessage
        {
            Type = "ClaimCreated",
            Content = JsonSerializer.Serialize(new { claim.Id, claim.PolicyId, claim.Amount, claim.Status, claim.FiledAt })
        };
        context.OutboxMessages.Add(outboxMessage);

        await context.SaveChangesAsync(cancellationToken);
        return claim.Id;
    }

    /// <summary>
    ///     Updates the status of an existing claim and records an outbox message.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="dto">The claim status update data transfer object containing the new status.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task UpdateStatus(int id, UpdateClaimStatusDto dto, CancellationToken cancellationToken = default)
    {
        var claim = await context.Claims.FindAsync([id], cancellationToken);
        if (claim == null) return;

        claim.Status = dto.Status;

        var outboxMessage = new OutboxMessage
        {
            Type = "ClaimStatusUpdated",
            Content = JsonSerializer.Serialize(new { claim.Id, claim.PolicyId, claim.Status })
        };
        context.OutboxMessages.Add(outboxMessage);

        await context.SaveChangesAsync(cancellationToken);
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClaimsController(AppDbContext context) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult> Create(ClaimDto dto)
    {
        var policyExists = await context.Policies.AnyAsync(p => p.Id == dto.PolicyId);
        if (!policyExists) return BadRequest("Policy does not exist.");

        var claim = new Claim
        {
            PolicyId = dto.PolicyId,
            Amount = dto.Amount,
            Status = ClaimStatus.Pending,
            FiledAt = DateTime.UtcNow
        };

        context.Claims.Add(claim);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = claim.Id }, claim);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDto>> GetById(int id)
    {
        var claim = await context.Claims
            .Include(c => c.Policy)
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

        return claim == null ? NotFound() : Ok(claim);
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult> UpdateStatus(int id, ClaimDto dto)
    {
        var claim = await context.Claims.FindAsync(id);
        if (claim == null) return NotFound();

        claim.Status = dto.Status;
        await context.SaveChangesAsync();
        return NoContent();
    }
}
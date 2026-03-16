using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PoliciesController(AppDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PolicyDto>>> GetAll([FromQuery] PolicyStatus? status)
    {
        var query = context.Policies.Include(p => p.PolicyHolder).AsQueryable();

        if (status != null) query = query.Where(p => p.Status == status);

        var policies = await query.Select(p => new PolicyDto
        {
            Id = p.Id,
            PolicyNumber = p.PolicyNumber,
            Premium = p.Premium,
            Status = p.Status,
            PolicyholderName = $"{p.PolicyHolder.FirstName} {p.PolicyHolder.LastName}"
        }).ToListAsync();

        return Ok(policies);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PolicyDto>> GetById(int id)
    {
        var policy = await context.Policies
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

        if (policy == null) return NotFound();

        return Ok(policy);
    }

    [HttpPost]
    public async Task<ActionResult> Create(CreatePolicyDto dto)
    {
        var policy = new Policy
        {
            Premium = dto.Premium,
            Status = PolicyStatus.Active,
            PolicyHolderId = dto.PolicyHolderId
        };

        context.Policies.Add(policy);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = policy.Id }, policy);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, UpdatePolicyDto dto)
    {
        var policy = await context.Policies.FindAsync(id);
        if (policy == null) return NotFound();

        policy.Status = dto.Status;
        policy.Premium = dto.Premium;
        await context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Cancel(int id)
    {
        var policy = await context.Policies.FindAsync(id);
        if (policy == null) return NotFound();

        policy.Status = PolicyStatus.Cancelled;
        await context.SaveChangesAsync();

        return NoContent();
    }
}
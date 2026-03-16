using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;

namespace PolicyManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PolicyHoldersController(AppDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PolicyHolderDto>>> GetAll()
    {
        List<PolicyHolderDto> holders = await context.PolicyHolders.Select(p => new PolicyHolderDto
        {
            Id = p.Id,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email
        }).ToListAsync();

        return Ok(holders);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PolicyHolderDto>> GetById(int id)
    {
        PolicyHolderDto? holder = await context.PolicyHolders.Where(p => p.Id == id)
            .Select(p => new PolicyHolderDto
                { Id = p.Id, FirstName = p.FirstName, LastName = p.LastName, Email = p.Email }).FirstOrDefaultAsync();

        if (holder == null)
        {
            return NotFound();
        }

        return Ok(holder);
    }

    [HttpPost]
    public async Task<ActionResult> Create(CreatePolicyHolderDto dto)
    {
        PolicyHolder holder = new PolicyHolder
            { FirstName = dto.FirstName, LastName = dto.LastName, Email = dto.Email };

        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();
        
        var result = new PolicyHolderDto
        {
            Id = holder.Id,
            FirstName = holder.FirstName,
            LastName = holder.LastName,
            Email = holder.Email
        };

        return CreatedAtAction(nameof(GetById), new { id = holder.Id }, result);
    }
}
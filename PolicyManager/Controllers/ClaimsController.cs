using Microsoft.AspNetCore.Mvc;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Services;

namespace PolicyManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClaimsController(AppDbContext context, IClaimsService claimsService, IPoliciesService policiesService)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClaimDto>>> GetAll()
    {
        var claims = await claimsService.GetAll();
        return Ok(claims);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDto>> GetById(int id)
    {
        var claim = await claimsService.GetById(id);
        return claim == null ? NotFound() : Ok(claim);
    }

    [HttpPost]
    public async Task<ActionResult> Create(ClaimDto dto)
    {
        var existingPolicy = await policiesService.GetById(dto.PolicyId);
        if (existingPolicy != null) return BadRequest("Policy does not exist.");

        var claimId = await claimsService.Create(dto);
        return CreatedAtAction(nameof(GetById), new { id = claimId }, claimId);
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult> UpdateStatus(int id, ClaimDto dto)
    {
        await claimsService.UpdateStatus(id, dto);
        return NoContent();
    }
}
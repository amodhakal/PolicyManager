using Microsoft.AspNetCore.Mvc;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;
using PolicyManager.Services;

namespace PolicyManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PoliciesController(AppDbContext context, IPoliciesService policiesService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PolicyDto>>> GetAll([FromQuery] PolicyStatus? status)
    {
        var policies = await policiesService.GetAll(status);
        return Ok(policies);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PolicyDto>> GetById(int id)
    {
        var policy = await policiesService.GetById(id);
        return policy == null ? NotFound() : Ok(policy);
    }

    [HttpPost]
    public async Task<ActionResult> Create(CreatePolicyDto dto)
    {
        var policyId = await policiesService.Create(dto);
        return CreatedAtAction(nameof(GetById), new { id = policyId }, policyId);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, UpdatePolicyDto dto)
    {
        await policiesService.Update(id, dto);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Cancel(int id)
    {
        await policiesService.Cancel(id);
        return NoContent();
    }
}
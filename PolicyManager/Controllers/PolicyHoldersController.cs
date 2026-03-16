using Microsoft.AspNetCore.Mvc;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Services;

namespace PolicyManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PolicyHoldersController(AppDbContext context, IPolicyHoldersService policyHoldersService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PolicyHolderDto>>> GetAll()
    {
        var holders = await policyHoldersService.GetAll();
        return Ok(holders);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PolicyHolderDto>> GetById(int id)
    {
        var holder = await policyHoldersService.GetById(id);
        return holder == null ? NotFound() : Ok(holder);
    }

    [HttpPost]
    public async Task<ActionResult> Create(CreatePolicyHolderDto dto)
    {
        var holderId = await policyHoldersService.Create(dto);
        return CreatedAtAction(nameof(GetById), new { id = holderId }, holderId);
    }
}
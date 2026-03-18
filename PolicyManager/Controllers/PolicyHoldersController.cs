using Microsoft.AspNetCore.Mvc;
using PolicyManager.DTOs;
using PolicyManager.Services;

namespace PolicyManager.Controllers;

/// <summary>
///     Controller for managing policy holders.
/// </summary>
/// <remarks>
///     Provides endpoints for retrieving and creating policyholders.
///     Each policyholder can own multiple insurance policies.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class PolicyHoldersController(IPolicyHoldersService policyHoldersService) : ControllerBase
{
    /// <summary>
    ///     Retrieves all policyholders.
    /// </summary>
    /// <returns>A list of all policyholders.</returns>
    /// <response code="200">Returns the list of policyholders.</response>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PolicyHolderDto>>> GetAll()
    {
        var holders = await policyHoldersService.GetAll();
        return Ok(holders);
    }

    /// <summary>
    ///     Retrieves a policyholder by their unique identifier.
    /// </summary>
    /// <param name="id">The policyholder identifier.</param>
    /// <returns>The policyholder if found; otherwise, not found.</returns>
    /// <response code="200">Returns the policyholder.</response>
    /// <response code="404">policyholder not found.</response>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PolicyHolderDto>> GetById(int id)
    {
        var holder = await policyHoldersService.GetById(id);
        return holder == null ? NotFound() : Ok(holder);
    }

    /// <summary>
    ///     Creates a new policyholder.
    /// </summary>
    /// <param name="dto">The policy holder data transfer object containing holder details.</param>
    /// <returns>A created result with the new policyholder's identifier.</returns>
    /// <response code="201">policyholder created successfully.</response>
    /// <response code="400">Invalid input or duplicate email.</response>
    [HttpPost]
    public async Task<ActionResult> Create(CreatePolicyHolderDto dto)
    {
        var holderId = await policyHoldersService.Create(dto);
        return CreatedAtAction(nameof(GetById), new { id = holderId }, holderId);
    }
}
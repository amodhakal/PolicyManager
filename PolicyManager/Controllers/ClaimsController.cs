using Microsoft.AspNetCore.Mvc;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Services;

namespace PolicyManager.Controllers;

/// <summary>
///     Controller for managing insurance claims.
/// </summary>
/// <remarks>
///     Provides endpoints for retrieving, creating, and updating claims.
///     All endpoints require a valid policy reference.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class ClaimsController(AppDbContext context, IClaimsService claimsService, IPoliciesService policiesService)
    : ControllerBase
{
    /// <summary>
    ///     Retrieves all claims.
    /// </summary>
    /// <returns>A list of all claims.</returns>
    /// <response code="200">Returns the list of claims.</response>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClaimDto>>> GetAll()
    {
        var claims = await claimsService.GetAll();
        return Ok(claims);
    }

    /// <summary>
    ///     Retrieves a claim by its unique identifier.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <returns>The claim if found; otherwise, not found.</returns>
    /// <response code="200">Returns the claim.</response>
    /// <response code="404">Claim not found.</response>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDto>> GetById(int id)
    {
        var claim = await claimsService.GetById(id);
        return claim == null ? NotFound() : Ok(claim);
    }

    /// <summary>
    ///     Creates a new claim.
    /// </summary>
    /// <param name="dto">The claim data transfer object containing claim details.</param>
    /// <returns>A created result with the new claim's identifier.</returns>
    /// <response code="201">Claim created successfully.</response>
    /// <response code="400">Policy does not exist.</response>
    [HttpPost]
    public async Task<ActionResult> Create(ClaimDto dto)
    {
        var existingPolicy = await policiesService.GetById(dto.PolicyId);
        if (existingPolicy != null) return BadRequest("Policy does not exist.");

        var claimId = await claimsService.Create(dto);
        return CreatedAtAction(nameof(GetById), new { id = claimId }, claimId);
    }

    /// <summary>
    ///     Updates the status of an existing claim.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="dto">The claim data containing the new status.</param>
    /// <returns>No content if successful.</returns>
    /// <response code="204">Status updated successfully.</response>
    /// <response code="404">Claim not found.</response>
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult> UpdateStatus(int id, ClaimDto dto)
    {
        await claimsService.UpdateStatus(id, dto);
        return NoContent();
    }
}
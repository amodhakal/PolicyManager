using Microsoft.AspNetCore.Mvc;
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;
using PolicyManager.Services;

namespace PolicyManager.Controllers;

/// <summary>
///     Controller for managing insurance policies.
/// </summary>
/// <remarks>
///     Provides endpoints for retrieving, creating, updating, and canceling policies.
///     Each policy is associated with a specific policyholder.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class PoliciesController(IPoliciesService policiesService) : ControllerBase
{
    /// <summary>
    ///     Retrieves all policies, optionally filtered by status.
    /// </summary>
    /// <param name="status">Optional status filter for policies.</param>
    /// <returns>A list of policies matching the filter criteria.</returns>
    /// <response code="200">Returns the list of policies.</response>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PolicyDto>>> GetAll([FromQuery] PolicyStatus? status)
    {
        var policies = await policiesService.GetAll(status);
        return Ok(policies);
    }

    /// <summary>
    ///     Retrieves a policy by its unique identifier.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <returns>The policy if found; otherwise, not found.</returns>
    /// <response code="200">Returns the policy.</response>
    /// <response code="404">Policy not found.</response>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PolicyDto>> GetById(int id)
    {
        var policy = await policiesService.GetById(id);
        return policy == null ? NotFound() : Ok(policy);
    }

    /// <summary>
    ///     Creates a new policy.
    /// </summary>
    /// <param name="dto">The policy data transfer object containing policy details.</param>
    /// <returns>A created result with the new policy's identifier.</returns>
    /// <response code="201">Policy created successfully.</response>
    /// <response code="400">Invalid input or policy holder not found.</response>
    [HttpPost]
    public async Task<ActionResult> Create(CreatePolicyDto dto)
    {
        var policyId = await policiesService.Create(dto);
        return CreatedAtAction(nameof(GetById), new { id = policyId }, policyId);
    }

    /// <summary>
    ///     Updates an existing policy.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="dto">The policy data transfer object containing updated details.</param>
    /// <returns>No content if successful.</returns>
    /// <response code="204">Policy updated successfully.</response>
    /// <response code="404">Policy not found.</response>
    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, UpdatePolicyDto dto)
    {
        await policiesService.Update(id, dto);
        return NoContent();
    }

    /// <summary>
    ///     Cancels an existing policy.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <returns>No content if successful.</returns>
    /// <response code="204">Policy canceled successfully.</response>
    /// <response code="404">Policy not found.</response>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Cancel(int id)
    {
        await policiesService.Cancel(id);
        return NoContent();
    }
}
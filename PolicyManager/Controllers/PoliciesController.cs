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
    ///     Retrieves a page of policies, optionally filtered by status.
    /// </summary>
    /// <remarks>
    ///     Supports <c>?page=</c>, <c>?pageSize=</c>, <c>?sortBy=</c> and <c>?descending=</c>. The page
    ///     size is capped at <see cref="PaginationQuery.MaxPageSize" />; an out-of-range value is clamped
    ///     rather than rejected. An unrecognised <c>sortBy</c> falls back to ordering by identifier.
    /// </remarks>
    /// <param name="pagination">The requested page, page size and sort.</param>
    /// <param name="status">Optional status filter for policies.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of policies matching the filter criteria.</returns>
    /// <response code="200">Returns the requested page of policies and the total matching count.</response>
    [HttpGet]
    public async Task<ActionResult<PagedResult<PolicyDto>>> GetAll(
        [FromQuery] PaginationQuery pagination,
        [FromQuery] PolicyStatus? status,
        CancellationToken cancellationToken)
    {
        var policies = await policiesService.GetAll(pagination, status, cancellationToken);
        return Ok(policies);
    }

    /// <summary>
    ///     Retrieves a policy by its unique identifier.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The policy if found; otherwise, not found.</returns>
    /// <response code="200">Returns the policy.</response>
    /// <response code="404">Policy not found.</response>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PolicyDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var policy = await policiesService.GetById(id, cancellationToken);
        return policy == null ? NotFound() : Ok(policy);
    }

    /// <summary>
    ///     Creates a new policy.
    /// </summary>
    /// <param name="dto">The policy data transfer object containing policy details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A created result whose body is the new policy.</returns>
    /// <response code="201">Policy created successfully.</response>
    /// <response code="404">The referenced policyholder does not exist.</response>
    [HttpPost]
    public async Task<ActionResult<PolicyDto>> Create(CreatePolicyDto dto, CancellationToken cancellationToken)
    {
        var policyId = await policiesService.Create(dto, cancellationToken);
        var policy = await policiesService.GetById(policyId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = policyId }, policy);
    }

    /// <summary>
    ///     Updates an existing policy.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="dto">The policy data transfer object containing updated details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>No content if successful.</returns>
    /// <response code="200">Policy updated successfully.</response>
    /// <response code="404">Policy not found.</response>
    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, UpdatePolicyDto dto, CancellationToken cancellationToken)
    {
        await policiesService.Update(id, dto, cancellationToken);
        return Ok();
    }

    /// <summary>
    ///     Cancels an existing policy.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>No content if successful.</returns>
    /// <response code="200">Policy canceled successfully.</response>
    /// <response code="404">Policy not found.</response>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        await policiesService.Cancel(id, cancellationToken);
        return Ok();
    }
}

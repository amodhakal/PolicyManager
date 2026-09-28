using Microsoft.AspNetCore.Mvc;
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
public class ClaimsController(IClaimsService claimsService, IPoliciesService policiesService)
    : ControllerBase
{
    /// <summary>
    ///     Retrieves a page of claims.
    /// </summary>
    /// <remarks>
    ///     Supports <c>?page=</c>, <c>?pageSize=</c>, <c>?sortBy=</c> and <c>?descending=</c>. The page
    ///     size is capped at <see cref="PaginationQuery.MaxPageSize" />; an out-of-range value is clamped
    ///     rather than rejected. An unrecognised <c>sortBy</c> falls back to ordering by identifier.
    /// </remarks>
    /// <param name="pagination">The requested page, page size and sort.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of claims.</returns>
    /// <response code="200">Returns the requested page of claims and the total matching count.</response>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ClaimDto>>> GetAll(
        [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var claims = await claimsService.GetAll(pagination, cancellationToken);
        return Ok(claims);
    }

    /// <summary>
    ///     Retrieves a claim by its unique identifier.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The claim if found; otherwise, not found.</returns>
    /// <response code="200">Returns the claim.</response>
    /// <response code="404">Claim not found.</response>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var claim = await claimsService.GetById(id, cancellationToken);
        return claim == null ? NotFound() : Ok(claim);
    }

    /// <summary>
    ///     Creates a new claim.
    /// </summary>
    /// <param name="dto">The claim creation data transfer object containing claim details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A created result whose body is the new claim.</returns>
    /// <response code="201">Claim created successfully.</response>
    /// <response code="400">Policy does not exist.</response>
    [HttpPost]
    public async Task<ActionResult<ClaimDto>> Create(CreateClaimDto dto, CancellationToken cancellationToken)
    {
        if (!await policiesService.ExistsAsync(dto.PolicyId, cancellationToken)) return BadRequest("Policy does not exist.");

        var claimId = await claimsService.Create(dto, cancellationToken);
        var claim = await claimsService.GetById(claimId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = claimId }, claim);
    }

    /// <summary>
    ///     Updates the status of an existing claim.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="dto">The claim status update data transfer object containing the new status.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>No content if successful.</returns>
    /// <response code="200">Status updated successfully.</response>
    /// <response code="404">Claim not found.</response>
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult> UpdateStatus(int id, UpdateClaimStatusDto dto, CancellationToken cancellationToken)
    {
        await claimsService.UpdateStatus(id, dto, cancellationToken);
        return Ok();
    }
}

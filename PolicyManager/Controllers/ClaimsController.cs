using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PolicyManager.Configuration;
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
// The unversioned route is kept, not replaced. It is what every existing client already calls, and
// it resolves to 1.0 because the default version is assumed when none is supplied. Replacing it
// would be a breaking change dressed up as a version introduction.
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion(ApiVersions.V1)]
[Authorize(Policy = AuthorizationPolicies.AnyRole)]
public class ClaimsController(IClaimsService claimsService)
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
    ///     Files a claim against an active policy, subject to the policy's coverage limit.
    /// </summary>
    /// <param name="dto">The claim creation data transfer object containing claim details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A created result whose body is the new claim.</returns>
    /// <response code="201">Claim created successfully.</response>
    /// <response code="404">The referenced policy does not exist.</response>
    /// <response code="422">The policy is not active, or the claim exceeds its remaining coverage.</response>
    // Filing a claim is the front line's job, so every role may do it.
    [HttpPost]
    public async Task<ActionResult<ClaimDto>> Create(CreateClaimDto dto, CancellationToken cancellationToken)
    {
        // The policy existence and status checks live in the service, which needs the loaded policy
        // to evaluate the coverage rules anyway. Duplicating them here meant a second round trip and
        // a second, differently-shaped error for the same condition.
        var claimId = await claimsService.Create(dto, cancellationToken);
        var claim = await claimsService.GetById(claimId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = claimId }, claim);
    }

    /// <summary>
    ///     Adjudicates a claim, recording who decided it and any notes.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="dto">The adjudication body: the new status, and optionally the adjuster and notes.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>No content if successful.</returns>
    /// <response code="200">Status updated successfully.</response>
    /// <response code="400">The status is missing.</response>
    /// <response code="404">Claim not found.</response>
    /// <response code="422">The transition is not legal from the claim's current status.</response>
    /// <response code="401">No bearer token was presented.</response>
    /// <response code="403">The token does not carry a role allowed to adjudicate.</response>
    // Deciding a claim is not. An agent who files a claim must not be the one who approves it, so
    // adjudication requires a role that did not create it.
    [Authorize(Policy = AuthorizationPolicies.AdminOrAdjuster)]
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult> UpdateStatus(int id, UpdateClaimStatusDto dto, CancellationToken cancellationToken)
    {
        await claimsService.UpdateStatus(
            id, dto, dto.DecidedBy, dto.Notes, dto.RowVersion, cancellationToken);
        return Ok();
    }
}

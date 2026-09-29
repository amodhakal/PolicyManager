using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PolicyManager.Configuration;
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;
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
// The unversioned route is kept, not replaced. It is what every existing client already calls, and
// it resolves to 1.0 because the default version is assumed when none is supplied. Replacing it
// would be a breaking change dressed up as a version introduction.
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion(ApiVersions.V1)]
[Authorize(Policy = AuthorizationPolicies.AnyRole)]
public class PolicyHoldersController(IPolicyHoldersService policyHoldersService, IPoliciesService policiesService)
    : ControllerBase
{
    /// <summary>
    ///     Retrieves a page of policyholders.
    /// </summary>
    /// <remarks>
    ///     Supports <c>?page=</c>, <c>?pageSize=</c>, <c>?sortBy=</c> and <c>?descending=</c>. The page
    ///     size is capped at <see cref="PaginationQuery.MaxPageSize" />; an out-of-range value is clamped
    ///     rather than rejected. An unrecognised <c>sortBy</c> falls back to ordering by identifier.
    /// </remarks>
    /// <param name="pagination">The requested page, page size and sort.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of policyholders.</returns>
    /// <response code="200">Returns the requested page of policyholders and the total matching count.</response>
    [HttpGet]
    public async Task<ActionResult<PagedResult<PolicyHolderDto>>> GetAll(
        [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var holders = await policyHoldersService.GetAll(pagination, cancellationToken);
        return Ok(holders);
    }

    /// <summary>
    ///     Retrieves a policyholder by their unique identifier.
    /// </summary>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The policyholder if found; otherwise, not found.</returns>
    /// <response code="200">Returns the policyholder.</response>
    /// <response code="404">policyholder not found.</response>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PolicyHolderDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var holder = await policyHoldersService.GetById(id, cancellationToken);
        return holder == null ? NotFound() : Ok(holder);
    }

    /// <summary>
    ///     Retrieves a page of the policies a single policyholder owns.
    /// </summary>
    /// <remarks>
    ///     Supports <c>?page=</c>, <c>?pageSize=</c>, <c>?sortBy=</c>, <c>?descending=</c> and
    ///     <c>?status=</c>, exactly as the unfiltered policies list does. A policyholder who owns
    ///     nothing matching the filter returns an empty page; a policyholder who does not exist is a
    ///     404.
    /// </remarks>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="pagination">The requested page, page size and sort.</param>
    /// <param name="status">Optional status filter for policies.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of the policyholder's policies.</returns>
    /// <response code="200">Returns the requested page of policies and the total matching count.</response>
    /// <response code="404">policyholder not found.</response>
    [HttpGet("{id:int}/policies")]
    public async Task<ActionResult<PagedResult<PolicyDto>>> GetPolicies(
        int id,
        [FromQuery] PaginationQuery pagination,
        [FromQuery] PolicyStatus? status,
        CancellationToken cancellationToken)
    {
        var policies = await policiesService.GetByPolicyHolder(id, pagination, status, cancellationToken);
        return Ok(policies);
    }

    /// <summary>
    ///     Creates a new policyholder.
    /// </summary>
    /// <param name="dto">The policy holder data transfer object containing holder details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A created result whose body is the new policyholder.</returns>
    /// <response code="201">policyholder created successfully.</response>
    /// <response code="400">Invalid input.</response>
    /// <response code="401">No bearer token was presented.</response>
    /// <response code="403">The token does not carry a role allowed to do this.</response>
    /// <response code="409">A policyholder with the same email already exists.</response>
    // Registering a holder is back-office work, not something the front line does.
    [Authorize(Policy = AuthorizationPolicies.AdminOrAdjuster)]
    [HttpPost]
    public async Task<ActionResult<PolicyHolderDto>> Create(
        CreatePolicyHolderDto dto, CancellationToken cancellationToken)
    {
        var holderId = await policyHoldersService.Create(dto, cancellationToken);
        var holder = await policyHoldersService.GetById(holderId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = holderId }, holder);
    }

    /// <summary>
    ///     Updates an existing policyholder, applying only the fields supplied.
    /// </summary>
    /// <remarks>
    ///     A field omitted from the body is left alone. Supplying none at all is a 400 rather than a
    ///     silent no-op reported as success.
    /// </remarks>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="dto">The policyholder fields to change.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>No content body if successful.</returns>
    /// <response code="200">Policyholder updated successfully.</response>
    /// <response code="400">No field supplied, or a field is blank.</response>
    /// <response code="404">policyholder not found.</response>
    /// <response code="409">Another policyholder already holds the supplied email.</response>
    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, UpdatePolicyHolderDto dto, CancellationToken cancellationToken)
    {
        await policyHoldersService.Update(id, dto, cancellationToken);
        return Ok();
    }

    /// <summary>
    ///     Soft-deletes a policyholder: the row is kept and hidden from every read.
    /// </summary>
    /// <remarks>
    ///     The relationship from a policy to its holder cascades, so removing the holder outright would
    ///     take their policies and, through them, every claim ever filed against them. A claim is a
    ///     financial record carrying the adjudication trail, so that history is not destroyed by a
    ///     contact-record change: the row survives and the holder is hidden instead, leaving the
    ///     history intact and recoverable through <c>PATCH /api/policyholders/{id}/restore</c>.
    ///     <para>
    ///         The holder's policies stay in the book and keep naming them: only the holder's own
    ///         record disappears, so the policies remain visible in the policy list and the reports
    ///         while the holder is a 404 wherever they are addressed directly. Their address also
    ///         stays taken, because the unique index still covers the row.
    ///     </para>
    /// </remarks>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>No content body if successful.</returns>
    /// <response code="200">Policyholder deleted successfully.</response>
    /// <response code="404">policyholder not found.</response>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await policyHoldersService.Delete(id, cancellationToken);
        return Ok();
    }

    /// <summary>
    ///     Restores a soft-deleted policyholder, making them visible to every read again.
    /// </summary>
    /// <remarks>
    ///     Idempotent: restoring a policyholder who was never deleted changes nothing and succeeds.
    ///     A restore cannot collide with a duplicate address, because a soft-deleted holder's address
    ///     was never released to anybody else.
    /// </remarks>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The restored policyholder.</returns>
    /// <response code="200">Returns the policyholder, restored or already active.</response>
    /// <response code="404">policyholder not found.</response>
    [HttpPatch("{id:int}/restore")]
    public async Task<ActionResult<PolicyHolderDto>> Restore(int id, CancellationToken cancellationToken)
    {
        await policyHoldersService.Restore(id, cancellationToken);

        var holder = await policyHoldersService.GetById(id, cancellationToken);
        return Ok(holder);
    }
}

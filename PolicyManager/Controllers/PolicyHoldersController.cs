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
    ///     Creates a new policyholder.
    /// </summary>
    /// <param name="dto">The policy holder data transfer object containing holder details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A created result whose body is the new policyholder.</returns>
    /// <response code="201">policyholder created successfully.</response>
    /// <response code="400">Invalid input.</response>
    /// <response code="409">A policyholder with the same email already exists.</response>
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
    ///     Deletes a policyholder who owns no policies.
    /// </summary>
    /// <remarks>
    ///     A holder who still has policies is a 409 rather than a silent removal of them: their claims
    ///     are financial records and are not destroyed by a contact-record change.
    /// </remarks>
    /// <param name="id">The policyholder identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>No content body if successful.</returns>
    /// <response code="200">Policyholder deleted successfully.</response>
    /// <response code="404">policyholder not found.</response>
    /// <response code="409">The policyholder still owns policies.</response>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await policyHoldersService.Delete(id, cancellationToken);
        return Ok();
    }
}

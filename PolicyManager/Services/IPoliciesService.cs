using PolicyManager.DTOs;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

/// <summary>
///     Interface for managing insurance policies.
/// </summary>
public interface IPoliciesService
{
    /// <summary>
    ///     Retrieves one page of policies, optionally filtered by status, ordered, with the total
    ///     count of the whole filtered result set. Sorts by <c>id</c>, <c>policyNumber</c>,
    ///     <c>premium</c>, <c>status</c> or <c>policyHolderId</c>, defaulting to <c>id</c>.
    /// </summary>
    /// <param name="pagination">The requested page, page size and sort. Normalized by the service.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of policies matching the filter criteria.</returns>
    public Task<PagedResult<PolicyDto>> GetAll(
        PaginationQuery pagination, PolicyStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves one page of the policies a single policy holder owns, optionally filtered by
    ///     status. Sorts by <c>id</c>, <c>policyNumber</c>, <c>premium</c>, <c>status</c> or
    ///     <c>policyHolderId</c>, defaulting to <c>id</c>.
    /// </summary>
    /// <remarks>
    ///     A holder who exists but owns nothing matching the filter gets an empty page; a holder who
    ///     does not exist raises <c>NotFoundException</c>, so the two are never confused.
    /// </remarks>
    /// <param name="policyHolderId">The policy holder identifier.</param>
    /// <param name="pagination">The requested page, page size and sort. Normalized by the service.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of the policy holder's policies.</returns>
    /// <exception cref="Exceptions.NotFoundException">No policy holder has that identifier.</exception>
    public Task<PagedResult<PolicyDto>> GetByPolicyHolder(
        int policyHolderId,
        PaginationQuery pagination,
        PolicyStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves a policy by its unique identifier.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The policy if found; otherwise, null.</returns>
    public Task<PolicyDto?> GetById(int id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a new policy.
    /// </summary>
    /// <exception cref="Exceptions.NotFoundException">The referenced policyholder does not exist.</exception>
    /// <param name="dto">The policy data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created policy.</returns>
    public Task<int> Create(CreatePolicyDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates an existing policy.
    /// </summary>
    /// <exception cref="Exceptions.NotFoundException">The policy does not exist.</exception>
    /// <param name="id">The policy identifier.</param>
    /// <param name="dto">The policy data transfer object containing updated details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task Update(int id, UpdatePolicyDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Cancels an existing policy by setting its status to Cancel.
    /// </summary>
    /// <exception cref="Exceptions.NotFoundException">The policy does not exist.</exception>
    /// <param name="id">The policy identifier.</param>
    /// <param name="id">The policy identifier.</param>
    /// <param name="rowVersion">
    ///     The concurrency token the caller read, or null to cancel unconditionally. A mismatch is
    ///     reported as a conflict rather than cancelling a policy that has since changed.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task Cancel(
        int id,
        string? rowVersion = null,
        CancellationToken cancellationToken = default);
}

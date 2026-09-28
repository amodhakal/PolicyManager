using PolicyManager.DTOs;

namespace PolicyManager.Services;

/// <summary>
///     Interface for managing insurance claims.
/// </summary>
public interface IClaimsService
{
    /// <summary>
    ///     Retrieves one page of claims, ordered, with the total count of the whole result set.
    ///     Sorts by <c>id</c>, <c>claimNumber</c>, <c>amount</c>, <c>status</c>, <c>filedAt</c> or
    ///     <c>policyId</c>, defaulting to <c>id</c>.
    /// </summary>
    /// <param name="pagination">The requested page, page size and sort. Normalized by the service.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of claims as ClaimDto objects.</returns>
    public Task<PagedResult<ClaimDto>> GetAll(
        PaginationQuery pagination, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves a claim by its unique identifier.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The claim if found; otherwise, null.</returns>
    public Task<ClaimDto?> GetById(int id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Files a claim against a policy, subject to the policy's status and coverage limit.
    /// </summary>
    /// <exception cref="Exceptions.NotFoundException">The referenced policy does not exist.</exception>
    /// <exception cref="Exceptions.BusinessRuleException">
    ///     The policy is not active, or the claim exceeds its remaining coverage.
    /// </exception>
    /// <param name="dto">The claim creation data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created claim.</returns>
    public Task<int> Create(CreateClaimDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Adjudicates a claim, enforcing the legal status transitions.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="dto">The claim status update data transfer object containing the new status.</param>
    /// <param name="decidedBy">The identifier of the adjuster making the decision.</param>
    /// <param name="adjusterNotes">The adjuster's notes.</param>
    /// <param name="rowVersion">
    ///     The concurrency token the caller read, or null to decide unconditionally. Adjudication is
    ///     the step most likely to be attempted twice at once, so without it the second decision
    ///     quietly replaces the first.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task UpdateStatus(
        int id,
        UpdateClaimStatusDto dto,
        string? decidedBy = null,
        string? adjusterNotes = null,
        string? rowVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes a claim that has not been adjudicated.
    /// </summary>
    /// <remarks>
    ///     An approved or denied claim is a decision record and is refused; see the implementation.
    /// </remarks>
    /// <param name="id">The claim identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True when a claim was found and deleted; false when no claim has that identifier.</returns>
    /// <exception cref="Exceptions.ConflictException">The claim has already been adjudicated.</exception>
    public Task<bool> Delete(int id, CancellationToken cancellationToken = default);
}

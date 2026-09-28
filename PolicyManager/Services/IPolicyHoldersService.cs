using PolicyManager.DTOs;

namespace PolicyManager.Services;

/// <summary>
///     Interface for managing policy holders.
/// </summary>
public interface IPolicyHoldersService
{
    /// <summary>
    ///     Retrieves one page of policy holders, ordered, with the total count of the whole result
    ///     set. Sorts by <c>id</c>, <c>firstName</c>, <c>lastName</c> or <c>email</c>, defaulting to
    ///     <c>id</c>.
    /// </summary>
    /// <param name="pagination">The requested page, page size and sort. Normalized by the service.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of policy holders. Not served from the cache.</returns>
    public Task<PagedResult<PolicyHolderDto>> GetAll(
        PaginationQuery pagination, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves a policy holder by their unique identifier.
    /// </summary>
    /// <param name="id">The policy holder identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The policy holder if found; otherwise, null.</returns>
    public Task<PolicyHolderDto?> GetById(int id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a new policy holder.
    /// </summary>
    /// <exception cref="Exceptions.ConflictException">A policyholder with the same email already exists.</exception>
    /// <param name="dto">The policy holder data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created policy holder.</returns>
    public Task<int> Create(CreatePolicyHolderDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates an existing policy holder, applying only the fields that were supplied.
    /// </summary>
    /// <remarks>
    ///     A field left null is not written, so a caller changing only the last name does not blank
    ///     the first name and the email address.
    /// </remarks>
    /// <exception cref="Exceptions.NotFoundException">The policy holder does not exist.</exception>
    /// <exception cref="Exceptions.ConflictException">Another policy holder already holds the supplied email.</exception>
    /// <param name="id">The policy holder identifier.</param>
    /// <param name="dto">The policy holder fields to change.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task Update(int id, UpdatePolicyHolderDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes a policy holder who owns no policies.
    /// </summary>
    /// <exception cref="Exceptions.NotFoundException">The policy holder does not exist.</exception>
    /// <exception cref="Exceptions.ConflictException">The policy holder still owns policies.</exception>
    /// <param name="id">The policy holder identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task Delete(int id, CancellationToken cancellationToken = default);
}

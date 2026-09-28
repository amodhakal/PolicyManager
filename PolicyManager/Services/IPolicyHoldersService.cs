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
    /// <param name="dto">The policy holder data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created policy holder.</returns>
    public Task<int> Create(CreatePolicyHolderDto dto, CancellationToken cancellationToken = default);
}

using PolicyManager.DTOs;

namespace PolicyManager.Services;

/// <summary>
///     Interface for managing policy holders.
/// </summary>
public interface IPolicyHoldersService
{
    /// <summary>
    ///     Retrieves all policy holders, from the cache when a cached collection is present and from the database
    ///     otherwise.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A list of all policy holders.</returns>
    public Task<IEnumerable<PolicyHolderDto>> GetAll(CancellationToken cancellationToken = default);

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
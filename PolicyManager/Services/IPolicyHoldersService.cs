using PolicyManager.DTOs;

namespace PolicyManager.Services;

/// <summary>
///     Interface for managing policy holders.
/// </summary>
public interface IPolicyHoldersService
{
    /// <summary>
    ///     Retrieves all policy holders from the database.
    /// </summary>
    /// <returns>A list of all policy holders.</returns>
    public Task<IEnumerable<PolicyHolderDto>> GetAll();

    /// <summary>
    ///     Retrieves a policy holder by their unique identifier.
    /// </summary>
    /// <param name="id">The policy holder identifier.</param>
    /// <returns>The policy holder if found; otherwise, null.</returns>
    public Task<PolicyHolderDto?> GetById(int id);

    /// <summary>
    ///     Creates a new policy holder.
    /// </summary>
    /// <param name="dto">The policy holder data transfer object.</param>
    /// <returns>The unique identifier of the newly created policy holder.</returns>
    public Task<int> Create(CreatePolicyHolderDto dto);
}
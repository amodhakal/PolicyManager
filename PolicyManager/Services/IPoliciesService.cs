using PolicyManager.DTOs;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

/// <summary>
///     Interface for managing insurance policies.
/// </summary>
public interface IPoliciesService
{
    /// <summary>
    ///     Retrieves all policies, optionally filtered by status.
    /// </summary>
    /// <param name="status">Optional status filter.</param>
    /// <returns>A list of policies matching the filter criteria.</returns>
    public Task<IEnumerable<PolicyDto>> GetAll(PolicyStatus? status);

    /// <summary>
    ///     Retrieves a policy by its unique identifier.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <returns>The policy if found; otherwise, null.</returns>
    public Task<PolicyDto?> GetById(int id);

    /// <summary>
    ///     Creates a new policy.
    /// </summary>
    /// <param name="dto">The policy data transfer object.</param>
    /// <returns>The unique identifier of the newly created policy.</returns>
    public Task<int> Create(CreatePolicyDto dto);

    /// <summary>
    ///     Updates an existing policy.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="dto">The policy data transfer object containing updated details.</param>
    public Task Update(int id, UpdatePolicyDto dto);

    /// <summary>
    ///     Cancels an existing policy by setting its status to Cancel.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    public Task Cancel(int id);
}
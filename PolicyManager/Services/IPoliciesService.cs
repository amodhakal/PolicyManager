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
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A list of policies matching the filter criteria.</returns>
    public Task<IEnumerable<PolicyDto>> GetAll(PolicyStatus? status, CancellationToken cancellationToken = default);

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
    /// <param name="dto">The policy data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created policy.</returns>
    public Task<int> Create(CreatePolicyDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates an existing policy.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="dto">The policy data transfer object containing updated details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task Update(int id, UpdatePolicyDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Cancels an existing policy by setting its status to Cancel.
    /// </summary>
    /// <param name="id">The policy identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task Cancel(int id, CancellationToken cancellationToken = default);
}
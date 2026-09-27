using PolicyManager.DTOs;

namespace PolicyManager.Services;

/// <summary>
///     Interface for managing insurance claims.
/// </summary>
public interface IClaimsService
{
    /// <summary>
    ///     Retrieves all claims from the database.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A list of all claims as ClaimDto objects.</returns>
    public Task<IEnumerable<ClaimDto>> GetAll(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves a claim by its unique identifier.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The claim if found; otherwise, null.</returns>
    public Task<ClaimDto?> GetById(int id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a new claim.
    /// </summary>
    /// <param name="dto">The claim data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created claim.</returns>
    public Task<int> Create(ClaimDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates the status of an existing claim.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="dto">The claim data containing the new status.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task UpdateStatus(int id, ClaimDto dto, CancellationToken cancellationToken = default);
}
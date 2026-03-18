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
    /// <returns>A list of all claims as ClaimDto objects.</returns>
    public Task<IEnumerable<ClaimDto>> GetAll();

    /// <summary>
    ///     Retrieves a claim by its unique identifier.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <returns>The claim if found; otherwise, null.</returns>
    public Task<ClaimDto?> GetById(int id);

    /// <summary>
    ///     Creates a new claim.
    /// </summary>
    /// <param name="dto">The claim data transfer object.</param>
    /// <returns>The unique identifier of the newly created claim.</returns>
    public Task<int> Create(ClaimDto dto);

    /// <summary>
    ///     Updates the status of an existing claim.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="dto">The claim data containing the new status.</param>
    public Task UpdateStatus(int id, ClaimDto dto);
}
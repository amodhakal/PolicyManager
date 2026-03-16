using PolicyManager.DTOs;

namespace PolicyManager.Services;

public interface IClaimsService
{
    public Task<IEnumerable<ClaimDto>> GetAll();

    public Task<ClaimDto?> GetById(int id);

    public Task<int> Create(ClaimDto dto);

    public Task UpdateStatus(int id, ClaimDto dto);
}
using PolicyManager.DTOs;

namespace PolicyManager.Services;

public interface IPolicyHoldersService
{
    public Task<IEnumerable<PolicyHolderDto>> GetAll();

    public Task<PolicyHolderDto?> GetById(int id);

    public Task<int> Create(CreatePolicyHolderDto dto);
}
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

public interface IPoliciesService
{
    public Task<IEnumerable<PolicyDto>> GetAll(PolicyStatus? status);

    public Task<PolicyDto?> GetById(int id);

    public Task<int> Create(CreatePolicyDto dto);

    public Task Update(int id, UpdatePolicyDto dto);

    public Task Cancel(int id);
}
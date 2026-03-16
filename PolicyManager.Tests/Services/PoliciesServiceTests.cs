using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;
using PolicyManager.Services;

namespace PolicyManager.Tests.Services;

public class PoliciesServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly PoliciesService _policiesService;

    public PoliciesServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(opts);
        _policiesService = new PoliciesService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private async Task<PolicyHolder> SeedHolder()
    {
        var holder = new PolicyHolder { FirstName = "Jane", LastName = "Doe", Email = "jane@example.com" };
        _context.PolicyHolders.Add(holder);
        await _context.SaveChangesAsync();
        return holder;
    }

    private async Task<int> SeedPolicy(int holderId, PolicyStatus status = PolicyStatus.Active)
    {
        return await _policiesService.Create(new CreatePolicyDto { PolicyHolderId = holderId, Premium = 500m });
    }

    [Fact]
    public async Task Create_ReturnsNewId_AndPersists()
    {
        var holder = await SeedHolder();
        var dto = new CreatePolicyDto { PolicyHolderId = holder.Id, Premium = 750m };

        var id = await _policiesService.Create(dto);
        var saved = await _context.Policies.FindAsync(id);
        Assert.NotNull(saved);
        Assert.Equal(750m, saved.Premium);
        Assert.Equal(PolicyStatus.Active, saved.Status);
    }

    [Fact]
    public async Task Cancel_SetsCancelledStatus_RowStillExists()
    {
        var holder = await SeedHolder();
        var id = await SeedPolicy(holder.Id);
        await _policiesService.Cancel(id);

        var policy = await _context.Policies.FindAsync(id);
        Assert.NotNull(policy);
        Assert.Equal(PolicyStatus.Cancelled, policy.Status);
    }

    [Fact]
    public async Task Cancel_NonExistentId_DoesNotThrow()
    {
        var ex = await Record.ExceptionAsync(() => _policiesService.Cancel(99999));
        Assert.Null(ex);
    }

    [Fact]
    public async Task GetAll_FilterByStatus_ReturnsOnlyMatching()
    {
        var holder = await SeedHolder();
        var activeId = await SeedPolicy(holder.Id);
        var cancelledId = await SeedPolicy(holder.Id);
        await _policiesService.Cancel(cancelledId);

        var activeEnumerable = await _policiesService.GetAll(PolicyStatus.Active);
        var active = activeEnumerable.ToList();

        Assert.Single(active);
        Assert.Equal(activeId, active[0].Id);
    }

    [Fact]
    public async Task GetAll_NoFilter_ReturnsAll()
    {
        var holder = await SeedHolder();
        await SeedPolicy(holder.Id);
        await SeedPolicy(holder.Id);

        var all = await _policiesService.GetAll(null);
        Assert.Equal(2, all.Count());
    }

    [Fact]
    public async Task Update_ChangesPremiumAndStatus()
    {
        var holder = await SeedHolder();
        var id = await SeedPolicy(holder.Id);

        await _policiesService.Update(id, new UpdatePolicyDto { Premium = 999m, Status = PolicyStatus.Expired });

        var policy = await _context.Policies.FindAsync(id);
        Assert.Equal(999m, policy!.Premium);
        Assert.Equal(PolicyStatus.Expired, policy.Status);
    }

    [Fact]
    public async Task GetById_ReturnsCorrectDto_WithHolderName()
    {
        var holder = await SeedHolder();
        var id = await SeedPolicy(holder.Id);
        var dto = await _policiesService.GetById(id);

        Assert.NotNull(dto);
        Assert.Equal("Jane Doe", dto!.PolicyholderName);
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsNull()
    {
        var result = await _policiesService.GetById(99999);
        Assert.Null(result);
    }
}
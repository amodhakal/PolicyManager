using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;
using PolicyManager.Services;

namespace PolicyManager.Tests.Services;

/// <summary>
///     Unit tests for the ClaimsService class.
/// </summary>
public class ClaimsServiceTests : IDisposable
{
    private readonly ClaimsService _claimsService;
    private readonly AppDbContext _context;

    public ClaimsServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(opts);
        _claimsService = new ClaimsService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>
    ///     Seeds a test policy into the database.
    /// </summary>
    /// <returns>The ID of the created policy.</returns>
    private async Task<int> SeedPolicy()
    {
        var holder = new PolicyHolder { FirstName = "Jane", LastName = "Doe", Email = "jane@example.com" };
        _context.PolicyHolders.Add(holder);
        await _context.SaveChangesAsync();

        var policy = new Policy
        {
            PolicyHolderId = holder.Id,
            Premium = 500m,
            Status = PolicyStatus.Active
        };

        _context.Policies.Add(policy);
        await _context.SaveChangesAsync();
        return policy.Id;
    }

    /// <summary>
    ///     Seeds a test claim into the database.
    /// </summary>
    /// <param name="policyId">The policy ID to associate with the claim.</param>
    /// <param name="amount">The claim amount.</param>
    /// <returns>The ID of the created claim.</returns>
    private async Task<int> SeedClaim(int policyId, decimal amount = 1000m)
    {
        return await _claimsService.Create(new ClaimDto { PolicyId = policyId, Amount = amount });
    }

    /// <summary>
    ///     Verifies that creating a claim persists it with Pending status.
    /// </summary>
    [Fact]
    public async Task Create_PersistsClaim_WithPendingStatus()
    {
        var policyId = await SeedPolicy();
        var id = await _claimsService.Create(new ClaimDto { PolicyId = policyId, Amount = 2500m });
        var claim = await _context.Claims.FindAsync(id);

        Assert.NotNull(claim);
        Assert.Equal(ClaimStatus.Pending, claim.Status);
        Assert.Equal(2500m, claim.Amount);
        Assert.True(claim.FiledAt <= DateTime.UtcNow);
    }

    /// <summary>
    ///     Verifies that updating claim status to Approved persists the change.
    /// </summary>
    [Fact]
    public async Task UpdateStatus_Approved_PersistsChange()
    {
        var policyId = await SeedPolicy();
        var id = await SeedClaim(policyId);

        await _claimsService.UpdateStatus(id, new ClaimDto { Status = ClaimStatus.Approved });

        var claim = await _context.Claims.FindAsync(id);
        Assert.Equal(ClaimStatus.Approved, claim!.Status);
    }

    /// <summary>
    ///     Verifies that updating claim status to Denied persists the change.
    /// </summary>
    [Fact]
    public async Task UpdateStatus_Denied_PersistsChange()
    {
        var policyId = await SeedPolicy();
        var id = await SeedClaim(policyId);

        await _claimsService.UpdateStatus(id, new ClaimDto { Status = ClaimStatus.Denied });

        var claim = await _context.Claims.FindAsync(id);
        Assert.Equal(ClaimStatus.Denied, claim!.Status);
    }

    /// <summary>
    ///     Verifies that updating status for non-existent claim does not throw.
    /// </summary>
    [Fact]
    public async Task UpdateStatus_NonExistentId_DoesNotThrow()
    {
        var ex = await Record.ExceptionAsync(() =>
            _claimsService.UpdateStatus(99999, new ClaimDto { Status = ClaimStatus.Approved }));
        Assert.Null(ex);
    }

    /// <summary>
    ///     Verifies that GetById returns the correct DTO.
    /// </summary>
    [Fact]
    public async Task GetById_ReturnsCorrectDto()
    {
        var policyId = await SeedPolicy();
        var id = await SeedClaim(policyId, 300m);

        var dto = await _claimsService.GetById(id);

        Assert.NotNull(dto);
        Assert.Equal(policyId, dto!.PolicyId);
        Assert.Equal(300m, dto.Amount);
        Assert.Equal(ClaimStatus.Pending, dto.Status);
    }

    /// <summary>
    ///     Verifies that GetById returns null for non-existent ID.
    /// </summary>
    [Fact]
    public async Task GetById_NotFound_ReturnsNull()
    {
        var result = await _claimsService.GetById(99999);
        Assert.Null(result);
    }

    /// <summary>
    ///     Verifies that GetAll returns all claims.
    /// </summary>
    [Fact]
    public async Task GetAll_ReturnsAllClaims()
    {
        var policyId = await SeedPolicy();
        await SeedClaim(policyId);
        await SeedClaim(policyId);

        var all = await _claimsService.GetAll();
        Assert.Equal(2, all.Count());
    }
}
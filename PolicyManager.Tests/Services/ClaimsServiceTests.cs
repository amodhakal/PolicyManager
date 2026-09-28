using PolicyManager.DTOs;
using PolicyManager.Exceptions;
using PolicyManager.Models;
using PolicyManager.Models.Enums;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Services;

/// <summary>
///     Unit tests for the ClaimsService class.
/// </summary>
public class ClaimsServiceTests : ServiceTestBase
{
    private readonly ClaimsService _claimsService;

    public ClaimsServiceTests()
    {
        _claimsService = new ClaimsService(Context, Numbers);
    }

    /// <summary>
    ///     Seeds a test policy with a distinct business number, because the number is unique and
    ///     two policies sharing one would be indistinguishable in any assertion that reads it back.
    /// </summary>
    private int _policyNumber = 1;

    /// <summary>
    ///     Seeds a test policy into the database.
    /// </summary>
    /// <returns>The ID of the created policy.</returns>
    private async Task<int> SeedPolicy()
    {
        var holder = await SeedHolderEntityAsync();

        var policy = new Policy
        {
            PolicyHolderId = holder.Id,
            Premium = 500m,
            Status = PolicyStatus.Active,
            PolicyNumber = $"POL-2026-{_policyNumber++:D6}"
        };

        Context.Policies.Add(policy);
        await Context.SaveChangesAsync();
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
        return await _claimsService.Create(new CreateClaimDto { PolicyId = policyId, Amount = amount });
    }

    /// <summary>
    ///     Verifies that creating a claim persists it with Pending status.
    /// </summary>
    [Fact]
    public async Task Create_PersistsClaim_WithPendingStatus()
    {
        var policyId = await SeedPolicy();
        var id = await _claimsService.Create(new CreateClaimDto { PolicyId = policyId, Amount = 2500m });
        var claim = await Context.Claims.FindAsync(id);

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

        await _claimsService.UpdateStatus(id, new UpdateClaimStatusDto { Status = ClaimStatus.Approved });

        var claim = await Context.Claims.FindAsync(id);
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

        await _claimsService.UpdateStatus(id, new UpdateClaimStatusDto { Status = ClaimStatus.Denied });

        var claim = await Context.Claims.FindAsync(id);
        Assert.Equal(ClaimStatus.Denied, claim!.Status);
    }

    /// <summary>
    ///     Verifies that adjudicating a non-existent claim raises NotFoundException.
    /// </summary>
    /// <remarks>
    ///     This asserted the opposite: that the call completed silently. A silent completion is
    ///     indistinguishable at the call site from a successful write, which is how a missing entity
    ///     came to be reported to API callers as a 200.
    /// </remarks>
    [Fact]
    public async Task UpdateStatus_NonExistentId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _claimsService.UpdateStatus(99999, new UpdateClaimStatusDto { Status = ClaimStatus.Approved }));
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

        var all = await _claimsService.GetAll(new PaginationQuery());
        Assert.Equal(2, all.Items.Count);
    }
}

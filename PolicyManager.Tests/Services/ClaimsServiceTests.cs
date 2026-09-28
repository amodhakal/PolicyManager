using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    ///     A claim awaiting adjudication is removed outright.
    /// </summary>
    [Fact]
    public async Task Delete_PendingClaim_RemovesTheRow()
    {
        var policyId = await SeedPolicy();
        var id = await SeedClaim(policyId);

        var deleted = await _claimsService.Delete(id);

        Assert.True(deleted);
        Assert.Null(await Context.Claims.FindAsync(id));
    }

    /// <summary>
    ///     Deleting a claim that was never decided releases its amount back to the policy's coverage.
    /// </summary>
    [Fact]
    public async Task Delete_PendingClaim_ReleasesItsCoverage()
    {
        var policy = new Policy
        {
            PolicyHolderId = (await SeedHolderEntityAsync()).Id,
            Premium = 500m,
            Status = PolicyStatus.Active,
            CoverageLimit = 300m
        };

        Context.Policies.Add(policy);
        await Context.SaveChangesAsync();

        await SeedClaim(policy.Id, 200m);

        // The whole 300 of coverage is committed, so a second claim of 200 has to be rejected.
        await Assert.ThrowsAsync<BusinessRuleException>(() => SeedClaim(policy.Id, 200m));

        var pending = await Context.Claims.SingleAsync();
        await _claimsService.Delete(pending.Id);

        // With the pending claim gone the same claim is acceptable again.
        var id = await SeedClaim(policy.Id, 200m);
        Assert.True(id > 0);
    }

    /// <summary>
    ///     A delete of an identifier nobody holds reports that nothing was deleted, which the
    ///     controller turns into a 404.
    /// </summary>
    [Fact]
    public async Task Delete_NonExistentId_ReturnsFalse()
    {
        Assert.False(await _claimsService.Delete(99999));
    }

    /// <summary>
    ///     An approved claim is a decision record and survives the delete attempt.
    /// </summary>
    [Fact]
    public async Task Delete_ApprovedClaim_IsRefused()
    {
        var policyId = await SeedPolicy();
        var id = await SeedClaim(policyId);
        await _claimsService.UpdateStatus(id, new UpdateClaimStatusDto { Status = ClaimStatus.Approved });

        await Assert.ThrowsAsync<ConflictException>(() => _claimsService.Delete(id));

        var claim = await Context.Claims.FindAsync(id);
        Assert.NotNull(claim);
        Assert.Equal(ClaimStatus.Approved, claim.Status);
    }

    /// <summary>
    ///     A denied claim is refused for the same reason an approved one is: the decision is the record.
    /// </summary>
    [Fact]
    public async Task Delete_DeniedClaim_IsRefused()
    {
        var policyId = await SeedPolicy();
        var id = await SeedClaim(policyId);
        await _claimsService.UpdateStatus(id, new UpdateClaimStatusDto { Status = ClaimStatus.Denied });

        await Assert.ThrowsAsync<ConflictException>(() => _claimsService.Delete(id));

        Assert.NotNull(await Context.Claims.FindAsync(id));
    }

    /// <summary>
    ///     Deleting a claim writes the outbox message describing the removal.
    /// </summary>
    [Fact]
    public async Task Delete_WritesOutboxMessage()
    {
        var policyId = await SeedPolicy();
        var id = await SeedClaim(policyId, 250m);

        await _claimsService.Delete(id);

        var message = await Context.OutboxMessages.SingleAsync(m => m.Type == "ClaimDeleted");
        Assert.Null(message.ProcessedAt);
        Assert.Contains(id.ToString(), message.Content);
    }

    /// <summary>
    ///     A refused delete publishes nothing: the claim that was never removed must not be announced
    ///     as removed.
    /// </summary>
    [Fact]
    public async Task Delete_Refused_WritesNoOutboxMessage()
    {
        var policyId = await SeedPolicy();
        var id = await SeedClaim(policyId);
        await _claimsService.UpdateStatus(id, new UpdateClaimStatusDto { Status = ClaimStatus.Approved });

        Context.OutboxMessages.RemoveRange(Context.OutboxMessages);
        await Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => _claimsService.Delete(id));

        Assert.Empty(await Context.OutboxMessages.ToListAsync());
    }
}

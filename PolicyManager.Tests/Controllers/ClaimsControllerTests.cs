using System.Net;
using System.Net.Http.Json;
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Controllers;

public class ClaimsControllerTests : ApiIntegrationTestBase
{
    /// <summary>
    ///     Creating a claim against an existing policy returns 201 and a positive ID.
    /// </summary>
    [Fact]
    public async Task CreateClaim_ValidPolicy_ReturnsCreated()
    {
        var holderId = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holderId);

        var res = await Client.PostAsJsonAsync("/api/claims",
            new CreateClaimDto { PolicyId = policyId, Amount = 300m });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var id = await ReadCreatedIdAsync<ClaimDto>(res, c => c.Id);
        Assert.True(id > 0);
    }

    /// <summary>
    ///     Creating a claim against a non-existent policy returns 400.
    /// </summary>
    [Fact]
    public async Task CreateClaim_NonExistentPolicy_ReturnsBadRequest()
    {
        var res = await Client.PostAsJsonAsync("/api/claims",
            new CreateClaimDto { PolicyId = 99999, Amount = 300m });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    ///     GetAll returns all seeded claims.
    /// </summary>
    [Fact]
    public async Task GetAll_ReturnsAllClaims()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        await SeedClaimAsync(policyId);
        await SeedClaimAsync(policyId);

        var res = await Client.GetAsync("/api/claims");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var claims = await res.Content.ReadFromJsonAsync<PagedResult<ClaimDto>>();
        Assert.Equal(2, claims!.Items.Count);
    }

    /// <summary>
    ///     GetAll on an empty database returns an empty page, not 404.
    /// </summary>
    [Fact]
    public async Task GetAll_NoClaims_ReturnsEmptyList()
    {
        var res = await Client.GetAsync("/api/claims");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var claims = await res.Content.ReadFromJsonAsync<PagedResult<ClaimDto>>();
        Assert.Empty(claims!.Items);
        Assert.Equal(0, claims.TotalCount);
        Assert.False(claims.HasNext);
    }

    /// <summary>
    ///     GetAll sorts descending: the largest claim amount comes first, and a page size that covers
    ///     every row leaves nothing on a following page.
    /// </summary>
    [Fact]
    public async Task GetAll_SortedDescending_ReturnsLargestAmountLast()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        await SeedClaimAsync(policyId, 100m);
        await SeedClaimAsync(policyId, 900m);
        await SeedClaimAsync(policyId, 500m);

        var res = await Client.GetAsync("/api/claims?sortBy=amount&descending=true&pageSize=3");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var page = await res.Content.ReadFromJsonAsync<PagedResult<ClaimDto>>();
        Assert.Equal(3, page!.Items.Count);
        Assert.Equal(900m, page.Items[0].Amount);
        Assert.Equal(500m, page.Items[1].Amount);
        Assert.Equal(100m, page.Items[2].Amount);
        Assert.False(page.HasNext);
    }

    /// <summary>
    ///     GetById for an existing claim returns 200 with correct policy reference.
    /// </summary>
    [Fact]
    public async Task GetById_ExistingId_ReturnsCorrectDto()
    {
        var holderId = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holderId);
        var claimId = await SeedClaimAsync(policyId);

        var res = await Client.GetAsync($"/api/claims/{claimId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var dto = await res.Content.ReadFromJsonAsync<ClaimDto>();
        Assert.NotNull(dto);
        Assert.Equal(policyId, dto!.PolicyId);
        Assert.Equal(200m, dto.Amount);
    }

    /// <summary>
    ///     GetById for a non-existent ID returns 404.
    /// </summary>
    [Fact]
    public async Task GetById_NonExistentId_Returns404()
    {
        var res = await Client.GetAsync("/api/claims/99999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    ///     Updating claim status returns 200 and the change is persisted.
    /// </summary>
    [Fact]
    public async Task UpdateStatus_ValidClaim_ReturnsOkAndPersists()
    {
        var holderId = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holderId);
        var claimId = await SeedClaimAsync(policyId);

        var patchRes = await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Approved });
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        var getRes = await Client.GetAsync($"/api/claims/{claimId}");
        var dto = await getRes.Content.ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(ClaimStatus.Approved, dto!.Status);
    }
}

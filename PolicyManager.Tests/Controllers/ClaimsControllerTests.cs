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

        var claims = await res.Content.ReadFromJsonAsync<List<ClaimDto>>();
        Assert.Equal(2, claims!.Count);
    }

    /// <summary>
    ///     GetAll on an empty database returns an empty list, not 404.
    /// </summary>
    [Fact]
    public async Task GetAll_NoClaims_ReturnsEmptyList()
    {
        var res = await Client.GetAsync("/api/claims");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var claims = await res.Content.ReadFromJsonAsync<List<ClaimDto>>();
        Assert.Empty(claims!);
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
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
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
    ///     Creating a claim against a non-existent policy returns 404.
    /// </summary>
    /// <remarks>
    ///     Was 400, from the controller's own <c>BadRequest("Policy does not exist.")</c>. That check
    ///     moved into the service, which needs the loaded policy to evaluate the coverage rules
    ///     anyway, and it now raises <c>NotFoundException</c> - mapped to 404, which is the correct
    ///     status for a resource referenced by a valid request that does not exist.
    /// </remarks>
    [Fact]
    public async Task CreateClaim_NonExistentPolicy_ReturnsNotFound()
    {
        var res = await Client.PostAsJsonAsync("/api/claims",
            new CreateClaimDto { PolicyId = 99999, Amount = 300m });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
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

    /// <summary>
    ///     Adjudicating a claim that does not exist returns 404.
    /// </summary>
    /// <remarks>
    ///     The PATCH had no coverage for a missing claim, so the endpoint's 404 was documented and
    ///     unverified - the same gap that let the policy endpoints answer 200 for a missing policy.
    /// </remarks>
    [Fact]
    public async Task UpdateStatus_NonExistentClaim_Returns404()
    {
        var res = await Client.PatchAsJsonAsync("/api/claims/99999/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Approved });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }    ///     Deleting a claim awaiting adjudication removes it, so a later read is a 404 and the list no
    ///     Deleting a claim awaiting adjudication hides it, so a later read is a 404 and the list no
    ///     longer counts it.
    /// </summary>
    [Fact]
    public async Task Delete_PendingClaim_HidesIt()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        var claimId = await SeedClaimAsync(policyId);
        await SeedClaimAsync(policyId);

        var res = await Client.DeleteAsync($"/api/claims/{claimId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/claims/{claimId}")).StatusCode);

        var remaining = await (await Client.GetAsync("/api/claims")).Content
            .ReadFromJsonAsync<PagedResult<ClaimDto>>();
        Assert.Single(remaining!.Items);
        Assert.Equal(1, remaining.TotalCount);
    }

    /// <summary>
    ///     A hidden claim also drops out of its policy's claim search.
    /// </summary>
    [Fact]
    public async Task Delete_HidesTheClaimFromItsPolicySearch()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        var hidden = await SeedClaimAsync(policyId, 100m);
        await SeedClaimAsync(policyId, 200m);

        await Client.DeleteAsync($"/api/claims/{hidden}");

        var page = await (await Client.GetAsync($"/api/policies/{policyId}/claims")).Content
            .ReadFromJsonAsync<PagedResult<ClaimDto>>();
        Assert.Equal(1, page!.TotalCount);
        Assert.Equal(200m, Assert.Single(page.Items).Amount);
    }

    /// <summary>
    ///     A restore brings a hidden claim back, on its own and through its policy.
    /// </summary>
    [Fact]
    public async Task Restore_BringsTheClaimBack()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        var claimId = await SeedClaimAsync(policyId, 300m);
        await Client.DeleteAsync($"/api/claims/{claimId}");

        var res = await Client.PatchAsync($"/api/claims/{claimId}/restore", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var claim = await res.Content.ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(claimId, claim!.Id);
        Assert.Equal(300m, claim.Amount);
        Assert.Equal(ClaimStatus.Pending, claim.Status);

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/claims/{claimId}")).StatusCode);

        var page = await (await Client.GetAsync($"/api/policies/{policyId}/claims")).Content
            .ReadFromJsonAsync<PagedResult<ClaimDto>>();
        Assert.Equal(1, page!.TotalCount);
    }

    /// <summary>
    ///     Restoring a claim that was never deleted succeeds: the call is idempotent.
    /// </summary>
    [Fact]
    public async Task Restore_OfAnActiveClaim_Returns200()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        var claimId = await SeedClaimAsync(policyId);

        var res = await Client.PatchAsync($"/api/claims/{claimId}/restore", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    /// <summary>
    ///     Restoring an identifier nobody holds is a 404.
    /// </summary>
    [Fact]
    public async Task Restore_NonExistentClaim_Returns404()
    {
        var res = await Client.PatchAsync("/api/claims/99999/restore", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    ///     Deleting an identifier nobody holds is a 404.
    /// </summary>
    [Fact]
    public async Task Delete_NonExistentClaim_Returns404()
    {
        var res = await Client.DeleteAsync("/api/claims/99999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    ///     An adjudicated claim is refused, because the record of who decided it and why is the reason
    ///     it stays in the book.
    /// </summary>
    [Fact]
    public async Task Delete_ApprovedClaim_Returns409()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        var claimId = await SeedClaimAsync(policyId);

        await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Approved, DecidedBy = "adj-1" });

        var res = await Client.DeleteAsync($"/api/claims/{claimId}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        // The decision trail is intact rather than removed with the row.
        var dto = await (await Client.GetAsync($"/api/claims/{claimId}")).Content
            .ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(ClaimStatus.Approved, dto!.Status);
        Assert.Equal("adj-1", dto.DecidedBy);
    }

    /// <summary>
    ///     The refusal reaches the caller as ProblemDetails naming the decision, not a bare status.
    /// </summary>
    [Fact]
    public async Task Delete_DeniedClaim_ReturnsProblemDetails()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        var claimId = await SeedClaimAsync(policyId);

        await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Denied });

        var res = await Client.DeleteAsync($"/api/claims/{claimId}");

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);

        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(409, problem!.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Contains("Denied", problem.Detail);
    }

    /// <summary>
    ///     A policy's claim search returns only the claims filed against that policy.
    /// </summary>
    [Fact]
    public async Task GetByPolicy_ReturnsOnlyThatPolicysClaims()
    {
        var holderId = await SeedHolderAsync();
        var first = await SeedPolicyAsync(holderId);
        var second = await SeedPolicyAsync(holderId);

        await SeedClaimAsync(first, 100m);
        await SeedClaimAsync(first, 200m);
        await SeedClaimAsync(second, 300m);

        var res = await Client.GetAsync($"/api/policies/{first}/claims");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var page = await res.Content.ReadFromJsonAsync<PagedResult<ClaimDto>>();
        Assert.NotNull(page);
        Assert.Equal(2, page!.TotalCount);
        Assert.All(page.Items, c => Assert.Equal(first, c.PolicyId));
    }

    /// <summary>
    ///     A policy nobody has claimed against gets an empty page, not a 404.
    /// </summary>
    [Fact]
    public async Task GetByPolicy_PolicyWithNoClaims_ReturnsAnEmptyPage()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        var res = await Client.GetAsync($"/api/policies/{policyId}/claims");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var page = await res.Content.ReadFromJsonAsync<PagedResult<ClaimDto>>();
        Assert.NotNull(page);
        Assert.Empty(page!.Items);
        Assert.Equal(0, page.TotalCount);
    }

    /// <summary>
    ///     Searching the claims of a policy that does not exist is a 404, so an empty claim history is
    ///     never confused with the wrong policy.
    /// </summary>
    [Fact]
    public async Task GetByPolicy_NonExistentPolicy_Returns404()
    {
        var res = await Client.GetAsync("/api/policies/99999/claims");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    ///     The search pages and filters like the unfiltered list does.
    /// </summary>
    [Fact]
    public async Task GetByPolicy_PageAndStatusFilter_Compose()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        var approved = await SeedClaimAsync(policyId, 400m);
        await SeedClaimAsync(policyId, 100m);
        await SeedClaimAsync(policyId, 200m);

        await Client.PatchAsJsonAsync($"/api/claims/{approved}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Approved });

        var res = await Client.GetAsync(
            $"/api/policies/{policyId}/claims?status=Pending&page=2&pageSize=1&sortBy=amount");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var page = await res.Content.ReadFromJsonAsync<PagedResult<ClaimDto>>();
        Assert.NotNull(page);
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(2, page.TotalPages);

        // The two pending claims are 100 and 200, so ascending by amount the second page is the 200.
        Assert.Equal(200m, Assert.Single(page.Items).Amount);
        Assert.Equal(ClaimStatus.Pending, page.Items[0].Status);
        Assert.True(page.HasPrevious);
        Assert.False(page.HasNext);
    }
}

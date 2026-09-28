using System.Net;
using System.Net.Http.Json;
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Tests that the claim lifecycle is enforced end to end, through the API.
/// </summary>
/// <remarks>
///     The domain rules are unit-tested in <c>ClaimRulesTests</c>. What matters here is that the
///     service actually calls them, that the right status reaches the caller, and that a rejected
///     claim leaves nothing behind.
/// </remarks>
public class ClaimLifecycleTests : ApiIntegrationTestBase
{
    /// <summary>
    ///     A claim cannot be filed against a cancelled policy.
    /// </summary>
    /// <remarks>
    ///     The bug: only the policy's <em>existence</em> was checked, so a cancelled policy still
    ///     accepted claims — the check that mattered was not the one being made.
    /// </remarks>
    [Fact]
    public async Task A_claim_against_a_cancelled_policy_is_rejected()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        await Client.DeleteAsync($"/api/policies/{policyId}");

        var response = await Client.PostAsJsonAsync("/api/claims",
            new CreateClaimDto { PolicyId = policyId, Amount = 100m, Description = "late" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertMentionsRuleAsync(response, "policy-not-active");
    }

    /// <summary>
    ///     A claim against a policy that does not exist is a 404.
    /// </summary>
    [Fact]
    public async Task A_claim_against_a_missing_policy_is_not_found()
    {
        var response = await Client.PostAsJsonAsync("/api/claims",
            new CreateClaimDto { PolicyId = 999_999, Amount = 100m, Description = "ghost" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    ///     A claim exceeding the policy's coverage limit is rejected.
    /// </summary>
    [Fact]
    public async Task A_claim_over_the_coverage_limit_is_rejected()
    {
        var policyId = await SeedPolicyWithCoverageAsync(1000m);

        var ok = await Client.PostAsJsonAsync("/api/claims",
            new CreateClaimDto { PolicyId = policyId, Amount = 600m, Description = "first" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var tooMuch = await Client.PostAsJsonAsync("/api/claims",
            new CreateClaimDto { PolicyId = policyId, Amount = 600m, Description = "second" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMuch.StatusCode);
        await AssertMentionsRuleAsync(tooMuch, "coverage-exhausted");
    }

    /// <summary>
    ///     A denied claim releases its coverage so the policy can be claimed against again.
    /// </summary>
    [Fact]
    public async Task Denying_a_claim_releases_its_coverage()
    {
        var policyId = await SeedPolicyWithCoverageAsync(1000m);

        var claimId = await SeedClaimAsync(policyId, 1000m);

        var denied = await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new { status = "Denied", decidedBy = "adj-1" });
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);

        var again = await Client.PostAsJsonAsync("/api/claims",
            new CreateClaimDto { PolicyId = policyId, Amount = 1000m, Description = "second" });

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    /// <summary>
    ///     A pending claim may be approved.
    /// </summary>
    [Fact]
    public async Task A_pending_claim_may_be_approved()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        var response = await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new { status = "Approved", decidedBy = "adj-1" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    ///     A denied claim cannot be flipped back to approved.
    /// </summary>
    /// <remarks>
    ///     This is the regression test for the state machine. Before it, the endpoint assigned
    ///     whatever status it was given, so a denial could be silently reversed.
    /// </remarks>
    [Fact]
    public async Task A_denied_claim_cannot_be_reinstated()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        var denied = await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status", new { status = "Denied" });
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);

        var reinstate = await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reinstate.StatusCode);
        await AssertMentionsRuleAsync(reinstate, "illegal-claim-transition");
    }

    /// <summary>
    ///     An approved claim cannot be denied afterwards.
    /// </summary>
    [Fact]
    public async Task An_approved_claim_cannot_be_denied_afterwards()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        var approved = await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status", new { status = "Approved" });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var deny = await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status", new { status = "Denied" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, deny.StatusCode);
    }

    /// <summary>
    ///     A rejected transition leaves the claim exactly as it was.
    /// </summary>
    [Fact]
    public async Task A_rejected_transition_does_not_alter_the_claim()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new { status = "Denied", decidedBy = "adj-1", notes = "no damage found" });

        await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status", new { status = "Approved" });

        var after = await Client.GetAsync($"/api/claims/{claimId}");
        var claim = await after.Content.ReadFromJsonAsync<ClaimDto>();

        Assert.Equal(ClaimStatus.Denied, claim!.Status);
        Assert.Equal("adj-1", claim.DecidedBy);
        Assert.Equal("no damage found", claim.AdjusterNotes);
    }

    /// <summary>
    ///     Adjudication records who decided, when, and their notes.
    /// </summary>
    [Fact]
    public async Task Adjudication_records_the_adjuster_and_the_notes()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        var response = await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new { status = "Approved", decidedBy = "adj-42", notes = "photos verified" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await Client.GetAsync($"/api/claims/{claimId}");
        var claim = await after.Content.ReadFromJsonAsync<ClaimDto>();

        Assert.Equal(ClaimStatus.Approved, claim!.Status);
        Assert.Equal("adj-42", claim.DecidedBy);
        Assert.Equal("photos verified", claim.AdjusterNotes);
        Assert.NotNull(claim.DecisionDate);
    }

    /// <summary>
    ///     A pending claim has no decision trail.
    /// </summary>
    [Fact]
    public async Task A_freshly_filed_claim_has_no_decision_trail()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        var after = await Client.GetAsync($"/api/claims/{claimId}");
        var claim = await after.Content.ReadFromJsonAsync<ClaimDto>();

        Assert.Equal(ClaimStatus.Pending, claim!.Status);
        Assert.Null(claim.DecisionDate);
        Assert.Null(claim.DecidedBy);
        Assert.Null(claim.AdjusterNotes);
    }

    /// <summary>
    ///     Adjudicating a claim that does not exist is a 404.
    /// </summary>
    [Fact]
    public async Task Adjudicating_a_missing_claim_is_not_found()
    {
        var response = await Client.PatchAsJsonAsync("/api/claims/999999/status", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<int> SeedPolicyWithCoverageAsync(decimal coverageLimit)
    {
        var holderId = await SeedHolderAsync();

        var response = await Client.PostAsJsonAsync("/api/policies", new CreatePolicyDto
        {
            PolicyHolderId = holderId,
            Premium = 500m,
            Type = PolicyType.Auto,
            CoverageLimit = coverageLimit,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2027, 1, 1)
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadCreatedIdAsync<PolicyDto>(response, p => p.Id);
    }

    /// <summary>
    ///     Asserts the ProblemDetails body names the rule that was broken.
    /// </summary>
    /// <remarks>
    ///     The rule id is what lets a caller distinguish "the policy is cancelled" from "you have
    ///     exhausted the coverage", so the assertion is on the rule specifically rather than on the
    ///     status alone.
    /// </remarks>
    private static async Task AssertMentionsRuleAsync(HttpResponseMessage response, string rule)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(rule, body, StringComparison.OrdinalIgnoreCase);
    }
}

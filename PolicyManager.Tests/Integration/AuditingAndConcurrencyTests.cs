using System.Net;
using System.Net.Http.Json;
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Covers the audit columns and the caller-supplied concurrency token on the policy and claim
///     endpoints.
/// </summary>
/// <remarks>
///     The rejection of a mismatched token is observable here because the in-memory provider honours
///     an explicitly set original value, reporting zero affected rows and raising
///     <c>DbUpdateConcurrencyException</c>. What it cannot do is <em>issue</em> a token — it has no
///     <c>rowversion</c> column to maintain — so the round trip of reading one back and writing it
///     again is covered against real SQL Server by <see cref="ConcurrencyTokenTests" />.
/// </remarks>
public class AuditingAndConcurrencyTests : ApiIntegrationTestBase
{
    /// <summary>
    ///     The subject the default test client's token identifies, and therefore the actor these
    ///     tests expect to see recorded.
    /// </summary>
    private const string Subject = "audit-test-user";

    /// <inheritdoc />
    public override async Task InitializeAsync()
    {
        // Set before the client is created, since the subject is baked into its token.
        Factory.DefaultSubject = Subject;
        await base.InitializeAsync();
    }

    [Fact]
    public async Task An_untouched_policy_reports_no_update_timestamp()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        var policy = await GetPolicyAsync(policyId);

        Assert.Null(policy!.UpdatedAt);
        Assert.Null(policy.UpdatedBy);
    }

    [Fact]
    public async Task Updating_a_policy_stamps_the_update_fields()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        var response = await Client.PutAsJsonAsync($"/api/policies/{policyId}",
            new UpdatePolicyDto { Premium = 750m });
        response.EnsureSuccessStatusCode();

        var policy = await GetPolicyAsync(policyId);

        Assert.Equal(750m, policy!.Premium);
        Assert.NotNull(policy.UpdatedAt);

        // The actor comes from the authenticated token, not from a constant. Asserted against the
        // subject the factory's token carries, so a regression that made the audit fall back to
        // "system" for an authenticated write would fail here.
        Assert.Equal(Subject, policy.UpdatedBy);
    }

    [Fact]
    public async Task A_mismatched_row_version_is_reported_as_a_conflict()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        var response = await Client.PutAsJsonAsync($"/api/policies/{policyId}",
            new UpdatePolicyDto { Premium = 600m, RowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]) });

        // 409, not 500: the caller can resolve this by re-reading, and burying it among server
        // faults would both mislead them and hide a normal contention event from alerting.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var policy = await GetPolicyAsync(policyId);
        Assert.Equal(500m, policy!.Premium);
    }

    [Fact]
    public async Task A_malformed_row_version_is_rejected_rather_than_ignored()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        var response = await Client.PutAsJsonAsync($"/api/policies/{policyId}",
            new UpdatePolicyDto { Premium = 600m, RowVersion = "not-a-token" });

        // Coercing an unparseable token to "no token" would let a caller believe they had
        // concurrency protection when the write would succeed regardless.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Omitting_the_row_version_still_updates_unconditionally()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        var response = await Client.PutAsJsonAsync($"/api/policies/{policyId}",
            new UpdatePolicyDto { Premium = 600m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var policy = await GetPolicyAsync(policyId);
        Assert.Equal(600m, policy!.Premium);
    }

    [Fact]
    public async Task A_mismatched_row_version_does_not_cancel_the_policy()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        var token = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]);

        var response = await Client.DeleteAsync(
            $"/api/policies/{policyId}?rowVersion={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var policy = await GetPolicyAsync(policyId);
        Assert.Equal(PolicyStatus.Active, policy!.Status);
    }

    [Fact]
    public async Task A_mismatched_row_version_does_not_adjudicate_the_claim()
    {
        var claimId = await SeedClaimForNewPolicyAsync();
        var token = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]);

        var response = await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Approved, RowVersion = token });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var claim = await Client.GetFromJsonAsync<ClaimDto>($"/api/claims/{claimId}");
        Assert.Equal(ClaimStatus.Pending, claim!.Status);
        Assert.Null(claim.DecisionDate);
    }

    [Fact]
    public async Task Adjudicating_a_claim_stamps_the_update_fields()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        var response = await Client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Approved, DecidedBy = "adj-1" });
        response.EnsureSuccessStatusCode();

        var claim = await Client.GetFromJsonAsync<ClaimDto>($"/api/claims/{claimId}");

        Assert.NotNull(claim!.UpdatedAt);
        Assert.Equal(Subject, claim.UpdatedBy);
    }

    private async Task<PolicyDto?> GetPolicyAsync(int id)
        => await Client.GetFromJsonAsync<PolicyDto>($"/api/policies/{id}");
}

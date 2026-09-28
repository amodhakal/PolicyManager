using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Tests for how a partially specified request body is bound and applied.
/// </summary>
/// <remarks>
///     Every enum in this domain starts at a meaningful value — <see cref="PolicyType.Auto" /> is
///     <c>0</c>, <see cref="PolicyStatus.Active" /> is <c>0</c>, <see cref="ClaimStatus.Pending" />
///     is <c>0</c>. That is what made omission dangerous: a non-nullable property cannot tell "the
///     caller sent the first member" from "the caller sent nothing", so both bound to the same value.
///     These tests pin the distinction, and the partial-update cases double as the negative-path
///     coverage the suite previously lacked.
/// </remarks>
public class RequestBindingTests : ApiIntegrationTestBase
{
    /// <summary>
    ///     A policy body that omits <c>type</c> is rejected rather than silently issued as Auto.
    /// </summary>
    [Fact]
    public async Task Create_policy_without_a_type_is_rejected()
    {
        var holderId = await SeedHolderAsync();

        var response = await Client.PostAsJsonAsync("/api/policies", new
        {
            policyHolderId = holderId,
            premium = 500m,
            startDate = new DateTime(2026, 1, 1),
            endDate = new DateTime(2027, 1, 1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertValidationErrorForAsync(response, nameof(CreatePolicyDto.Type));
    }

    /// <summary>
    ///     An explicit <c>"Auto"</c> is still accepted, so the required field did not break callers.
    /// </summary>
    [Fact]
    public async Task Create_policy_with_an_explicit_first_member_is_accepted()
    {
        var holderId = await SeedHolderAsync();

        var response = await Client.PostAsJsonAsync("/api/policies", new CreatePolicyDto
        {
            PolicyHolderId = holderId, Premium = 500m, Type = PolicyType.Auto,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    ///     A claim status body that omits <c>status</c> is rejected rather than re-opening to Pending.
    /// </summary>
    [Fact]
    public async Task Claim_status_without_a_status_is_rejected()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        var response = await Client.PatchAsync($"/api/claims/{claimId}/status",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertValidationErrorForAsync(response, nameof(UpdateClaimStatusDto.Status));
    }

    /// <summary>
    ///     A PUT that supplies only a premium leaves the status alone.
    /// </summary>
    /// <remarks>
    ///     The bug this pins: <c>Status</c> is non-nullable and defaults to <c>Active</c>, so a caller
    ///     correcting a premium on a <em>cancelled</em> policy silently reinstated it.
    /// </remarks>
    [Fact]
    public async Task Update_with_only_a_premium_leaves_the_status_untouched()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        await Client.DeleteAsync($"/api/policies/{policyId}");
        var afterCancel = await ReadPolicyAsync(policyId);
        Assert.Equal(PolicyStatus.Cancelled, afterCancel.Status);

        var response = await Client.PutAsJsonAsync($"/api/policies/{policyId}",
            new { premium = 1234m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadPolicyAsync(policyId);
        Assert.Equal(1234m, updated.Premium);
        Assert.Equal(PolicyStatus.Cancelled, updated.Status);
    }

    /// <summary>
    ///     A PUT that supplies only a status leaves the premium alone.
    /// </summary>
    [Fact]
    public async Task Update_with_only_a_status_leaves_the_premium_untouched()
    {
        var policyId = await SeedPolicyAsync(await SeedHolderAsync(), premium: 750m);

        var response = await Client.PutAsJsonAsync($"/api/policies/{policyId}",
            new { status = PolicyStatus.Expired });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadPolicyAsync(policyId);
        Assert.Equal(750m, updated.Premium);
        Assert.Equal(PolicyStatus.Expired, updated.Status);
    }

    /// <summary>
    ///     A PUT supplying both fields still applies both, so the partial cases did not regress it.
    /// </summary>
    [Fact]
    public async Task Update_with_both_fields_applies_both()
    {
        var policyId = await SeedPolicyAsync(await SeedHolderAsync(), premium: 500m);

        var response = await Client.PutAsJsonAsync($"/api/policies/{policyId}",
            new UpdatePolicyDto { Premium = 999m, Status = PolicyStatus.Cancelled });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadPolicyAsync(policyId);
        Assert.Equal(999m, updated.Premium);
        Assert.Equal(PolicyStatus.Cancelled, updated.Status);
    }

    /// <summary>
    ///     A PUT changing nothing is a 400 rather than a no-op reported as success.
    /// </summary>
    [Fact]
    public async Task Update_with_no_fields_is_rejected()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        var response = await Client.PutAsJsonAsync($"/api/policies/{policyId}", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     An out-of-range premium is a 400, not a database overflow.
    /// </summary>
    [Fact]
    public async Task Update_with_a_zero_premium_is_rejected()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        var response = await Client.PutAsJsonAsync($"/api/policies/{policyId}",
            new { premium = 0m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     A premium beyond the storable column range is a 400 rather than a 500 overflow.
    /// </summary>
    [Fact]
    public async Task Update_with_an_oversized_premium_is_rejected()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        var response = await Client.PutAsJsonAsync($"/api/policies/{policyId}",
            new { premium = 100_000_000m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     An end date on or before the start date is rejected.
    /// </summary>
    [Fact]
    public async Task Create_policy_with_an_end_date_before_the_start_is_rejected()
    {
        var holderId = await SeedHolderAsync();

        var response = await Client.PostAsJsonAsync("/api/policies", new CreatePolicyDto
        {
            PolicyHolderId = holderId, Premium = 500m, Type = PolicyType.Auto,
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2026, 1, 1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     Malformed JSON is a 400, not an unhandled 500.
    /// </summary>
    [Fact]
    public async Task Malformed_json_is_rejected()
    {
        var response = await Client.PostAsync("/api/policies",
            new StringContent("{ this is not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     An unknown enum member is a 400 rather than a silent default.
    /// </summary>
    [Fact]
    public async Task Unknown_enum_member_is_rejected()
    {
        var holderId = await SeedHolderAsync();

        var response = await Client.PostAsJsonAsync("/api/policies", new
        {
            policyHolderId = holderId,
            premium = 500m,
            type = "Telepathic",
            startDate = new DateTime(2026, 1, 1),
            endDate = new DateTime(2027, 1, 1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<PolicyDto> ReadPolicyAsync(int id)
    {
        var response = await Client.GetAsync($"/api/policies/{id}");
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    /// <summary>
    ///     Asserts the 400 carries a validation error keyed by the given property.
    /// </summary>
    /// <remarks>
    ///     Asserting on the parsed <c>Errors</c> dictionary rather than a substring of the raw body.
    ///     A ProblemDetails body for any 400 already contains the literal text <c>"type"</c> (the RFC
    ///     link) and <c>"status"</c> (the numeric field), so a substring search passes for every 400
    ///     the API can produce - including one caused by an entirely different field. That version of
    ///     this assertion could not have failed, and would have kept passing with the <c>[Required]</c>
    ///     attributes deleted.
    /// </remarks>
    private static async Task AssertValidationErrorForAsync(HttpResponseMessage response, string propertyName)
    {
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotNull(problem!.Errors);

        Assert.True(
            problem.Errors.ContainsKey(propertyName),
            $"Expected a validation error for '{propertyName}'. Got: "
            + (problem.Errors.Count == 0
                ? "(none)"
                : string.Join(", ", problem.Errors.Keys)));
    }
}

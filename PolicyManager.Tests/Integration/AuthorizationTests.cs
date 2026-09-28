using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Covers who may do what, and — as importantly — that a token is actually required at all.
/// </summary>
/// <remarks>
///     Every test here presents a real, signed token that the production validation pipeline
///     evaluates. A stubbed authentication scheme would keep all of this green if the signature
///     check, the issuer check, the audience check, the lifetime check or the role claim mapping were
///     wrong, which is every part of the feature that is easy to get wrong.
/// </remarks>
public class AuthorizationTests : ApiIntegrationTestBase
{
    [Fact]
    public async Task An_endpoint_refuses_a_request_with_no_token()
    {
        var response = await UnauthenticatedClient.GetAsync("/api/policyholders");

        // 401: the caller has not proved who they are. Distinct from 403 because the remedy is
        // different — a client that gets 401 should obtain a token, not ask for more permission.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Every_endpoint_refuses_an_unauthenticated_request()
    {
        var holderId = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holderId);
        var claimId = await SeedClaimAsync(policyId);

        var paths = new[]
        {
            "/api/policyholders", $"/api/policyholders/{holderId}",
            "/api/policies", $"/api/policies/{policyId}",
            "/api/claims", $"/api/claims/{claimId}"
        };

        foreach (var path in paths)
        {
            var response = await UnauthenticatedClient.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_token_signed_with_the_wrong_key_is_refused()
    {
        using var client = CreateTokenClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Mint(signingKey: "a-different-key-that-is-also-long-enough-32b"));

        var response = await client.GetAsync("/api/policyholders");

        // A signature failure must not be distinguishable from no token at all, or it tells an
        // attacker their forgery was structurally correct.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_refused()
    {
        using var client = CreateTokenClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Mint(issuer: "some-other-service"));

        var response = await client.GetAsync("/api/policyholders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_audience_is_refused()
    {
        using var client = CreateTokenClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Mint(audience: "some-other-api"));

        var response = await client.GetAsync("/api/policyholders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        using var client = CreateTokenClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Mint(expiresIn: TimeSpan.FromMinutes(-30)));

        var response = await client.GetAsync("/api/policyholders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_with_no_known_role_is_refused()
    {
        using var client = CreateTokenClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Mint(roles: ["Auditor"]));

        var response = await client.GetAsync("/api/policyholders");

        // A valid signature proves the token was minted here, not that it may do anything. A token
        // carrying a role this service has never heard of authenticates as a user with no
        // permissions, which reads as a permissions bug rather than a configuration one.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Every_role_may_read()
    {
        foreach (var role in PolicyRoles.All)
        {
            using var client = CreateClient(role);

            var response = await client.GetAsync("/api/policyholders");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task An_agent_may_not_adjudicate_a_claim()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        using var client = CreateClient(PolicyRoles.Agent);
        var response = await client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Approved });

        // The whole point of an adjuster being a different person: the agent who filed the claim
        // must not also be the one who approves it.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_adjuster_may_adjudicate_a_claim()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        using var client = CreateClient(PolicyRoles.Adjuster);
        var response = await client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Approved });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_may_adjudicate_a_claim()
    {
        var claimId = await SeedClaimForNewPolicyAsync();

        using var client = CreateClient(PolicyRoles.Admin);
        var response = await client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Approved });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_agent_may_file_a_claim()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        using var client = CreateClient(PolicyRoles.Agent);
        var response = await client.PostAsJsonAsync("/api/claims",
            new CreateClaimDto { PolicyId = policyId, Amount = 50m });

        // Filing is the front line's job, so every role may do it.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_agent_may_not_create_a_policy()
    {
        var holderId = await SeedHolderAsync();

        using var client = CreateClient(PolicyRoles.Agent);
        var response = await client.PostAsJsonAsync("/api/policies", NewPolicy(holderId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_adjuster_may_create_a_policy()
    {
        var holderId = await SeedHolderAsync();

        using var client = CreateClient(PolicyRoles.Adjuster);
        var response = await client.PostAsJsonAsync("/api/policies", NewPolicy(holderId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_adjuster_may_not_cancel_a_policy()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        using var client = CreateClient(PolicyRoles.Adjuster);
        var response = await client.DeleteAsync($"/api/policies/{policyId}");

        // Cancelling ends cover and may have to be honoured retroactively. That is a commercial
        // decision, not an operational one, so it is narrower than the other writes.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_may_cancel_a_policy()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        using var client = CreateClient(PolicyRoles.Admin);
        var response = await client.DeleteAsync($"/api/policies/{policyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_agent_may_not_register_a_policyholder()
    {
        using var client = CreateClient(PolicyRoles.Agent);
        var response = await client.PostAsJsonAsync("/api/policyholders", new CreatePolicyHolderDto
        {
            FirstName = "Reg", LastName = "istered", Email = "reg@test"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_adjuster_may_register_a_policyholder()
    {
        using var client = CreateClient(PolicyRoles.Adjuster);
        var response = await client.PostAsJsonAsync("/api/policyholders", new CreatePolicyHolderDto
        {
            FirstName = "Reg", LastName = "istered", Email = "reg@test"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_authenticated_write_records_the_token_subject_as_the_actor()
    {
        var policyId = await SeedPolicyForNewHolderAsync();

        using var client = CreateClient(PolicyRoles.Adjuster, "adj-42");
        await client.PutAsJsonAsync($"/api/policies/{policyId}", new UpdatePolicyDto { Premium = 900m });

        var policy = await Client.GetFromJsonAsync<PolicyDto>($"/api/policies/{policyId}");

        // The audit trail and the authorization decision come from the same token, so who was
        // permitted and who is recorded cannot drift apart.
        Assert.Equal("adj-42", policy!.UpdatedBy);
    }

    [Fact]
    public async Task A_refusal_is_distinguishable_from_a_missing_token()
    {
        using var client = CreateClient(PolicyRoles.Agent);
        var forbidden = await client.DeleteAsync("/api/policies/1");

        var unauthorized = await UnauthenticatedClient.DeleteAsync("/api/policies/1");

        // 403 means "we know who you are and the answer is no"; 401 means "prove who you are". A
        // client that conflates them either gives up or retries forever.
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    private HttpClient CreateClient(string role, string subject = "test-user")
        => CreateTokenClient([role], subject);

    private static CreatePolicyDto NewPolicy(int holderId) => new()
    {
        Type = PolicyType.Auto,
        PolicyHolderId = holderId,
        Premium = 500m,
        StartDate = new DateTime(2026, 1, 1),
        EndDate = new DateTime(2027, 1, 1)
    };
}

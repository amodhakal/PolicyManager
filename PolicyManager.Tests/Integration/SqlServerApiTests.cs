using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PolicyManager.DTOs;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     End-to-end tests that drive the real HTTP pipeline against a real SQL Server.
/// </summary>
/// <remarks>
///     Most of the suite runs on the in-memory provider, which skips the application's own service
///     registrations and the EF migrations. These tests prove the SQL Server wiring works at all:
///     the host starts, the migrated schema accepts the application's writes, and a round trip
///     through the API returns what was written.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public class SqlServerApiTests : SqlServerTestBase, IAsyncLifetime
{
    private readonly SqlServerApiFactory _factory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqlServerApiTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared container fixture.</param>
    public SqlServerApiTests(SqlServerFixture fixture) : base(fixture)
    {
        _factory = new SqlServerApiFactory(fixture);
    }

    /// <summary>
    ///     Gets the client used to talk to the test server.
    /// </summary>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    ///     Creates the client and clears the shared database.
    /// </summary>
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Client = _factory.CreateClient();
    }

    /// <summary>
    ///     Disposes the client and the test host.
    /// </summary>
    public override async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>
    ///     A policyholder written through the API is readable back from the migrated schema.
    /// </summary>
    [Fact]
    public async Task Create_then_read_a_policyholder_round_trips_through_sql_server()
    {
        var created = await Client.PostAsJsonAsync("/api/policyholders",
            new CreatePolicyHolderDto { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.com" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await created.Content.ReadFromJsonAsync<PolicyHolderDto>();
        Assert.NotNull(body);
        Assert.Equal("ada@example.com", body!.Email);

        var fetched = await Client.GetAsync($"/api/policyholders/{body.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        var fetchedBody = await fetched.Content.ReadFromJsonAsync<PolicyHolderDto>();
        Assert.Equal("Ada", fetchedBody!.FirstName);
        Assert.Equal("Lovelace", fetchedBody.LastName);
    }

    /// <summary>
    ///     A policy and a claim filed against it persist with the values that were submitted.
    /// </summary>
    [Fact]
    public async Task Policy_and_claim_persist_the_submitted_values()
    {
        var holderId = await CreateHolderAsync("Grace", "Hopper", "grace@example.com");

        var policyResponse = await Client.PostAsJsonAsync("/api/policies", new CreatePolicyDto
        {
            PolicyHolderId = holderId,
            Premium = 750.25m,
            Type = Models.Enums.PolicyType.Home,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2027, 3, 1)
        });

        Assert.Equal(HttpStatusCode.Created, policyResponse.StatusCode);
        var policy = await policyResponse.Content.ReadFromJsonAsync<PolicyDto>();

        var claimResponse = await Client.PostAsJsonAsync("/api/claims", new CreateClaimDto
        {
            PolicyId = policy!.Id, Amount = 120.50m, Description = "Storm damage to the roof"
        });

        Assert.Equal(HttpStatusCode.Created, claimResponse.StatusCode);
        var claim = await claimResponse.Content.ReadFromJsonAsync<ClaimDto>();

        Assert.Equal(120.50m, claim!.Amount);
        Assert.Equal("Storm damage to the roof", claim.Description);
        Assert.Equal(Models.Enums.ClaimStatus.Pending, claim.Status);
    }

    /// <summary>
    ///     Taking another holder's email address is a 409 the caller can resolve, on the same terms as
    ///     registering it in the first place.
    /// </summary>
    /// <remarks>
    ///     Needs SQL Server: the in-memory provider has no unique index, so the collision would be
    ///     stored silently there and the test would pass for the wrong reason.
    /// </remarks>
    [Fact]
    public async Task Updating_a_holder_to_a_taken_email_is_a_conflict()
    {
        await CreateHolderAsync("Grace", "Hopper", "grace@example.com");
        var secondId = await CreateHolderAsync("Alan", "Turing", "alan@example.com");

        var response = await Client.PutAsJsonAsync($"/api/policyholders/{secondId}",
            new UpdatePolicyHolderDto { Email = "grace@example.com" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // The loser of the collision is left exactly as it was.
        var unchanged = await (await Client.GetAsync($"/api/policyholders/{secondId}")).Content
            .ReadFromJsonAsync<PolicyHolderDto>();
        Assert.Equal("alan@example.com", unchanged!.Email);
    }

    /// <summary>
    ///     A hard delete on the real schema removes the holder's row rather than leaving it orphaned.
    /// </summary>
    /// <remarks>
    ///     Needs SQL Server: cascade behaviour is the database's, and the in-memory provider enforces
    ///     no foreign keys at all.
    /// </remarks>
    [Fact]
    public async Task Deleting_a_holder_removes_their_row_from_sql_server()
    {
        var holderId = await CreateHolderAsync("Edsger", "Dijkstra", "edsger@example.com");

        var response = await Client.DeleteAsync($"/api/policyholders/{holderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/policyholders/{holderId}")).StatusCode);
    }

    /// <summary>
    ///     A holder whose policy carries claims can be soft-deleted, and the claims are still there
    ///     afterwards.
    /// </summary>
    /// <remarks>
    ///     Needs SQL Server: this is the case the cascade would have destroyed, so the guard is only
    ///     meaningful where the cascade exists.
    /// </remarks>
    [Fact]
    public async Task Deleting_a_holder_with_claimed_policies_leaves_the_claims_in_sql_server()
    {
        var holderId = await CreateHolderAsync("Barbara", "Liskov", "barbara@example.com");

        var policyResponse = await Client.PostAsJsonAsync("/api/policies", new CreatePolicyDto
        {
            PolicyHolderId = holderId,
            Premium = 500m,
            Type = Models.Enums.PolicyType.Life,
            CoverageLimit = 1000m,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2027, 3, 1)
        });

        var policy = await policyResponse.Content.ReadFromJsonAsync<PolicyDto>();

        var claimResponse = await Client.PostAsJsonAsync("/api/claims", new CreateClaimDto
        {
            PolicyId = policy!.Id, Amount = 400m, Description = "Hospital costs"
        });

        Assert.Equal(HttpStatusCode.Created, claimResponse.StatusCode);
        var claim = await claimResponse.Content.ReadFromJsonAsync<ClaimDto>();

        var response = await Client.DeleteAsync($"/api/policyholders/{holderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The holder is hidden, and the policy and the claim behind it are untouched.
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/policyholders/{holderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/policies/{policy.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/claims/{claim!.Id}")).StatusCode);

        await using var context = CreateContext();
        var stillThere = await context.Claims.SingleAsync(c => c.Id == claim.Id);
        Assert.False(stillThere.IsDeleted);
    }

    /// <summary>
    ///     The report endpoints answer from the real provider, which is the only way to know the
    ///     grouped aggregates behind them translate to SQL rather than being quietly evaluated in
    ///     memory.
    /// </summary>
    /// <remarks>
    ///     The in-memory provider has no SQL to translate, so a query the SQL Server provider rejects
    ///     passes there every time.
    /// </remarks>
    [Fact]
    public async Task Report_endpoints_group_in_sql_server()
    {
        var holderId = await CreateHolderAsync("Margaret", "Hamilton", "margaret@example.com");

        var auto = await Client.PostAsJsonAsync("/api/policies", new CreatePolicyDto
        {
            PolicyHolderId = holderId, Premium = 1000m, Type = Models.Enums.PolicyType.Auto,
            StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2027, 3, 1)
        });
        var home = await Client.PostAsJsonAsync("/api/policies", new CreatePolicyDto
        {
            PolicyHolderId = holderId, Premium = 250m, Type = Models.Enums.PolicyType.Home,
            StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2027, 3, 1)
        });

        var claim = await Client.PostAsJsonAsync("/api/claims", new CreateClaimDto
        {
            PolicyId = (await auto.Content.ReadFromJsonAsync<PolicyDto>())!.Id,
            Amount = 500m,
            Description = "Collision"
        });

        await Client.PatchAsJsonAsync(
            $"/api/claims/{(await claim.Content.ReadFromJsonAsync<ClaimDto>())!.Id}/status",
            new UpdateClaimStatusDto { Status = Models.Enums.ClaimStatus.Approved });

        var premium = await (await Client.GetAsync("/api/reports/premium-by-type"))
            .Content.ReadFromJsonAsync<PremiumByTypeReportDto>();

        Assert.Equal(2, premium!.TotalPolicies);
        Assert.Equal(1250m, premium.TotalPremium);
        Assert.Equal(1000m, premium.Types.Single(t => t.Type == Models.Enums.PolicyType.Auto).TotalPremium);
        Assert.Equal(250m, premium.Types.Single(t => t.Type == Models.Enums.PolicyType.Home).TotalPremium);

        var open = await (await Client.GetAsync("/api/reports/open-claims-by-status"))
            .Content.ReadFromJsonAsync<OpenClaimsByStatusReportDto>();

        Assert.Equal(1, open!.TotalOpenClaims);
        Assert.Equal(500m, open.TotalOpenAmount);
        Assert.Equal(
            1, open.Statuses.Single(s => s.Status == Models.Enums.ClaimStatus.Approved).ClaimCount);
        Assert.Equal(0, open.Statuses.Single(s => s.Status == Models.Enums.ClaimStatus.Pending).ClaimCount);

        var ratio = await (await Client.GetAsync("/api/reports/claims-ratio-per-holder"))
            .Content.ReadFromJsonAsync<PagedResult<HolderClaimsRatioDto>>();

        var row = Assert.Single(ratio!.Items);
        Assert.Equal(holderId, row.PolicyHolderId);
        Assert.Equal(2, row.PolicyCount);
        Assert.Equal(1250m, row.TotalPremium);
        Assert.Equal(500m, row.TotalOpenClaimAmount);
        Assert.Equal(0.4m, row.ClaimsRatio);
        Assert.Equal("Margaret Hamilton", row.PolicyholderName);
        Assert.NotNull(home);
    }

    /// <summary>
    ///     A soft-deleted holder and claim keep their rows in the migrated schema, and a restore brings
    ///     them back.
    /// </summary>
    /// <remarks>
    ///     Needs SQL Server for two reasons the in-memory provider cannot answer: the migration has to
    ///     apply, and the soft delete has to be checked against the table rather than against a
    ///     change-tracker view. The policies of a deleted holder also have to keep resolving the
    ///     holder's name, which is why the read filter is applied per query rather than globally.
    /// </remarks>
    [Fact]
    public async Task Soft_delete_keeps_the_rows_and_restore_brings_them_back()
    {
        var holderId = await CreateHolderAsync("Katherine", "Johnson", "katherine@example.com");

        var policyResponse = await Client.PostAsJsonAsync("/api/policies", new CreatePolicyDto
        {
            PolicyHolderId = holderId, Premium = 600m, Type = Models.Enums.PolicyType.Home,
            CoverageLimit = 2000m, StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2027, 3, 1)
        });

        var policy = (await policyResponse.Content.ReadFromJsonAsync<PolicyDto>())!;

        var claimResponse = await Client.PostAsJsonAsync("/api/claims", new CreateClaimDto
        {
            PolicyId = policy.Id, Amount = 300m, Description = "Flood damage"
        });

        var claim = (await claimResponse.Content.ReadFromJsonAsync<ClaimDto>())!;

        Assert.Equal(HttpStatusCode.OK, (await Client.DeleteAsync($"/api/policyholders/{holderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.DeleteAsync($"/api/claims/{claim.Id}")).StatusCode);

        await using (var context = CreateContext())
        {
            var storedHolder = await context.PolicyHolders
                .IgnoreQueryFilters().SingleAsync(h => h.Id == holderId);
            Assert.True(storedHolder.IsDeleted);
            Assert.NotNull(storedHolder.DeletionDate);

            var storedClaim = await context.Claims.IgnoreQueryFilters().SingleAsync(c => c.Id == claim.Id);
            Assert.True(storedClaim.IsDeleted);
            Assert.NotNull(storedClaim.DeletionDate);
        }

        // Neither is visible through the API any more.
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/policyholders/{holderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/claims/{claim.Id}")).StatusCode);

        // The policy is still in the book and still names its holder: only the holder's own record was
        // hidden. This is the case a global filter on PolicyHolders would have broken.
        var stillReadable = await (await Client.GetAsync($"/api/policies/{policy.Id}")).Content
            .ReadFromJsonAsync<PolicyDto>();
        Assert.Equal("Katherine Johnson", stillReadable!.PolicyholderName);
        Assert.Equal(holderId, stillReadable.PolicyHolderId);

        // The hidden claim has released its coverage, so the full limit is available again.
        var secondClaim = await Client.PostAsJsonAsync("/api/claims", new CreateClaimDto
        {
            PolicyId = policy.Id, Amount = 1700m, Description = "Roof and contents"
        });
        Assert.Equal(HttpStatusCode.Created, secondClaim.StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await Client.PatchAsync($"/api/policyholders/{holderId}/restore", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await Client.PatchAsync($"/api/claims/{claim.Id}/restore", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/policyholders/{holderId}")).StatusCode);

        var restoredClaim = await (await Client.GetAsync($"/api/claims/{claim.Id}")).Content
            .ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(300m, restoredClaim!.Amount);
        Assert.Equal(Models.Enums.ClaimStatus.Pending, restoredClaim.Status);

        await using (var context = CreateContext())
        {
            var restoredHolder = await context.PolicyHolders.SingleAsync(h => h.Id == holderId);
            Assert.False(restoredHolder.IsDeleted);
            Assert.Null(restoredHolder.DeletionDate);
        }
    }

    private async Task<int> CreateHolderAsync(string first, string last, string email)
    {
        var response = await Client.PostAsJsonAsync("/api/policyholders",
            new CreatePolicyHolderDto { FirstName = first, LastName = last, Email = email });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PolicyHolderDto>();
        return dto!.Id;
    }
}

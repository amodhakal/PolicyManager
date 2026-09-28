using System.Net;
using System.Net.Http.Json;
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

    private async Task<int> CreateHolderAsync(string first, string last, string email)
    {
        var response = await Client.PostAsJsonAsync("/api/policyholders",
            new CreatePolicyHolderDto { FirstName = first, LastName = last, Email = email });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PolicyHolderDto>();
        return dto!.Id;
    }
}

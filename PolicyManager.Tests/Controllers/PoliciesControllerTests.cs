using System.Net;
using System.Net.Http.Json;
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Controllers;

public class PoliciesControllerTests : ApiIntegrationTestBase
{
    private readonly CreatePolicyHolderDto _createPolicyHolderDto =
        new() { FirstName = "Jane", LastName = "Doe", Email = "janedoe@gmail.com" };

    /// <summary>
    ///     Gets the policyholder used by the seeding helpers.
    /// </summary>
    protected override CreatePolicyHolderDto DefaultHolder => _createPolicyHolderDto;

    /// <summary>
    ///     Creating a policy with valid data returns 200/201 and a positive integer ID.
    /// </summary>
    [Fact]
    public async Task CreatePolicy_ValidRequest_ReturnsId()
    {
        var holderId = await SeedHolderAsync();
        var dto = new CreatePolicyDto { Type = Models.Enums.PolicyType.Auto, PolicyHolderId = holderId, Premium = 750m, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1) };

        var res = await Client.PostAsJsonAsync("/api/policies", dto);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var id = await ReadCreatedIdAsync<PolicyDto>(res, p => p.Id);
        Assert.True(id > 0);
    }

    /// <summary>
    ///     Creating a policy actually persists it — GET by ID returns the same premium.
    /// </summary>
    [Fact]
    public async Task CreatePolicy_Persists_CanBeRetrieved()
    {
        var id = await SeedPolicyForNewHolderAsync(999m);

        var res = await Client.GetAsync($"/api/policies/{id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var dto = await res.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(dto);
        Assert.Equal(999m, dto.Premium);
    }

    /// <summary>
    ///     GetAll with no filter returns every policy.
    /// </summary>
    [Fact]
    public async Task GetAll_NoFilter_ReturnsAll()
    {
        var holderId = await SeedHolderAsync();
        await SeedPolicyAsync(holderId);
        await SeedPolicyAsync(holderId);

        var res = await Client.GetAsync("/api/policies");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var policies = await res.Content.ReadFromJsonAsync<PagedResult<PolicyDto>>();
        Assert.Equal(2, policies!.Items.Count);
        Assert.Equal(2, policies.TotalCount);
    }

    /// <summary>
    ///     GetAll filtered by Active status returns only active policies.
    /// </summary>
    [Fact]
    public async Task GetAll_FilterByActive_ReturnsOnlyActive()
    {
        var holderId = await SeedHolderAsync();
        var activeId = await SeedPolicyAsync(holderId);
        var cancelledId = await SeedPolicyAsync(holderId);

        await Client.DeleteAsync($"/api/policies/{cancelledId}");

        var res = await Client.GetAsync("/api/policies?status=Active");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var policies = await res.Content.ReadFromJsonAsync<PagedResult<PolicyDto>>();
        var policy = Assert.Single(policies!.Items);
        Assert.Equal(activeId, policy.Id);
    }

    /// <summary>
    ///     GetAll filtered by Canceled status returns only canceled policies.
    /// </summary>
    [Fact]
    public async Task GetAll_FilterByCancelled_ReturnsOnlyCancelled()
    {
        var holderId = await SeedHolderAsync();
        await SeedPolicyAsync(holderId);
        var cancelledId = await SeedPolicyAsync(holderId);

        await Client.DeleteAsync($"/api/policies/{cancelledId}");

        var res = await Client.GetAsync("/api/policies?status=Cancelled");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var policies = await res.Content.ReadFromJsonAsync<PagedResult<PolicyDto>>();
        var policy = Assert.Single(policies!.Items);
        Assert.Equal(cancelledId, policy.Id);
    }

    /// <summary>
    ///     The status filter and pagination compose: TotalCount counts the filtered set only, and the
    ///     requested page slices it.
    /// </summary>
    [Fact]
    public async Task GetAll_StatusFilterWithPagination_Combines()
    {
        var holderId = await SeedHolderAsync();
        var firstActive = await SeedPolicyAsync(holderId, 100m);
        await SeedPolicyAsync(holderId, 200m);
        var cancelledId = await SeedPolicyAsync(holderId, 300m);

        await Client.DeleteAsync($"/api/policies/{cancelledId}");

        var res = await Client.GetAsync("/api/policies?status=Active&page=2&pageSize=1&sortBy=premium&descending=true");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var page = await res.Content.ReadFromJsonAsync<PagedResult<PolicyDto>>();
        Assert.NotNull(page);
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasPrevious);
        Assert.False(page.HasNext);
        Assert.Equal(firstActive, Assert.Single(page.Items).Id);
    }

    /// <summary>
    ///     GetById for an existing policy returns 200 with correct holder name.
    /// </summary>
    [Fact]
    public async Task GetById_ExistingId_ReturnsCorrectDto()
    {
        var holderId = await SeedHolderAsync();
        var id = await SeedPolicyAsync(holderId);

        var res = await Client.GetAsync($"/api/policies/{id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var dto = await res.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.Equal("Jane Doe", dto.PolicyholderName);
        Assert.Equal(PolicyStatus.Active, dto.Status);
    }

    /// <summary>
    ///     GetById for a non-existent ID returns 404.
    /// </summary>
    [Fact]
    public async Task GetById_NonExistentId_Returns404()
    {
        var res = await Client.GetAsync("/api/policies/99999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    ///     Updating premium and status persists both changes.
    /// </summary>
    [Fact]
    public async Task Update_ValidRequest_ChangesPremiumAndStatus()
    {
        var holderId = await SeedHolderAsync();
        var id = await SeedPolicyAsync(holderId);

        var updateDto = new UpdatePolicyDto { Premium = 1200m, Status = PolicyStatus.Expired };
        var res = await Client.PutAsJsonAsync($"/api/policies/{id}", updateDto);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var getRes = await Client.GetAsync($"/api/policies/{id}");
        var dto = await getRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.Equal(1200m, dto!.Premium);
        Assert.Equal(PolicyStatus.Expired, dto.Status);
    }

    /// <summary>
    ///     Cancelling an existing policy returns 200 and the policy status becomes Canceled.
    /// </summary>
    [Fact]
    public async Task Cancel_ExistingPolicy_ReturnsCancelledStatus()
    {
        var holderId = await SeedHolderAsync();
        var id = await SeedPolicyAsync(holderId);

        var deleteRes = await Client.DeleteAsync($"/api/policies/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        var getRes = await Client.GetAsync($"/api/policies/{id}");
        var dto = await getRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.Equal(PolicyStatus.Cancelled, dto!.Status);
    }

    /// <summary>
    ///     Updating a non-existent policy returns 404, not a 200 for a write that changed nothing.
    /// </summary>
    [Fact]
    public async Task Update_NonExistentId_Returns404()
    {
        var res = await Client.PutAsJsonAsync("/api/policies/99999",
            new UpdatePolicyDto { Premium = 100m, Status = PolicyStatus.Expired });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    ///     Cancelling a non-existent policy returns 404.
    /// </summary>
    /// <remarks>
    ///     This asserted only <c>&lt; 500</c>, which the broken behaviour satisfied: the service
    ///     returned without writing and the controller replied 200. The documented contract is 404,
    ///     so that is what is asserted.
    /// </remarks>
    [Fact]
    public async Task Cancel_NonExistentId_Returns404()
    {
        var res = await Client.DeleteAsync("/api/policies/99999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    ///     Creating a policy for a holder that does not exist returns 404.
    /// </summary>
    /// <remarks>
    ///     The reference is part of a well-formed request, so the caller has nothing to fix beyond
    ///     the identifier - which is what distinguishes this from the 409 a constraint violation
    ///     would have produced.
    /// </remarks>
    [Fact]
    public async Task Create_NonExistentHolder_Returns404()
    {
        var res = await Client.PostAsJsonAsync("/api/policies",
            new CreatePolicyDto
            {
                Type = Models.Enums.PolicyType.Auto, PolicyHolderId = 99999, Premium = 500m,
                StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
            });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

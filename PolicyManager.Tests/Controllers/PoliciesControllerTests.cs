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
        var dto = new CreatePolicyDto { PolicyHolderId = holderId, Premium = 750m };

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

        var policies = await res.Content.ReadFromJsonAsync<List<PolicyDto>>();
        Assert.Equal(2, policies!.Count);
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

        var policies = await res.Content.ReadFromJsonAsync<List<PolicyDto>>();
        Assert.Single(policies!);
        Assert.Equal(activeId, policies![0].Id);
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

        var policies = await res.Content.ReadFromJsonAsync<List<PolicyDto>>();
        Assert.Single(policies!);
        Assert.Equal(cancelledId, policies![0].Id);
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
        Assert.NotNull(dto);
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
    ///     Cancelling a non-existent policy does not return a 5xx — service swallows it.
    /// </summary>
    [Fact]
    public async Task Cancel_NonExistentId_DoesNotReturn5xx()
    {
        var res = await Client.DeleteAsync("/api/policies/99999");
        Assert.True((int)res.StatusCode < 500);
    }
}
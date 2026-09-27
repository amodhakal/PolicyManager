using System.Net;
using System.Net.Http.Json;
using PolicyManager.DTOs;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Controllers;

public class PolicyHoldersControllerTests : ApiIntegrationTestBase
{
    private readonly CreatePolicyHolderDto _createPolicyHolderDto =
        new() { FirstName = "Jane", LastName = "Doe", Email = "jd@gmail.com" };

    [Fact]
    public async Task Create_ValidHolder_Returns201WithId()
    {
        var response = await Client.PostAsJsonAsync("/api/policyholders", _createPolicyHolderDto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = await ReadCreatedIdAsync<PolicyHolderDto>(response, h => h.Id);
        Assert.True(id > 0);
    }

    [Fact]
    public async Task GetAll()
    {
        var emptyHolderResponse = await Client.GetAsync("/api/policyholders");

        Assert.Equal(HttpStatusCode.OK, emptyHolderResponse.StatusCode);
        var emptyHolders = await emptyHolderResponse.Content.ReadFromJsonAsync<IEnumerable<PolicyHolderDto>>();
        Assert.NotNull(emptyHolders);
        Assert.Empty(emptyHolders);


        await SeedHolderAsync(_createPolicyHolderDto);
        var holderResponse = await Client.GetAsync("/api/policyholders");

        Assert.Equal(HttpStatusCode.OK, holderResponse.StatusCode);
        var holders = (await holderResponse.Content.ReadFromJsonAsync<IEnumerable<PolicyHolderDto>>() ??
                       []).ToList();
        Assert.NotNull(holders);
        Assert.Single(holders);

        var holder = holders.First();
        Assert.Equal(_createPolicyHolderDto.FirstName, holder.FirstName);
        Assert.Equal(_createPolicyHolderDto.LastName, holder.LastName);
        Assert.Equal(_createPolicyHolderDto.Email, holder.Email);
    }

    [Fact]
    public async Task GetById_ExistingHolder_Returns200WithData()
    {
        var id = await SeedHolderAsync(_createPolicyHolderDto);
        var response = await Client.GetAsync($"/api/policyholders/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var holder = await response.Content.ReadFromJsonAsync<PolicyHolderDto>();
        Assert.NotNull(holder);
        Assert.Equal(_createPolicyHolderDto.FirstName, holder.FirstName);
        Assert.Equal(_createPolicyHolderDto.Email, holder.Email);
    }

    [Fact]
    public async Task GetById_NonExistentHolder_Returns404()
    {
        var response = await Client.GetAsync("/api/policyholders/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
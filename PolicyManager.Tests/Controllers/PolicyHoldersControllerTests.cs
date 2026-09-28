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
        var emptyHolders = await emptyHolderResponse.Content.ReadFromJsonAsync<PagedResult<PolicyHolderDto>>();
        Assert.NotNull(emptyHolders);
        Assert.Empty(emptyHolders!.Items);
        Assert.Equal(0, emptyHolders.TotalCount);
        Assert.Equal(0, emptyHolders.TotalPages);
        Assert.False(emptyHolders.HasNext);
        Assert.False(emptyHolders.HasPrevious);


        await SeedHolderAsync(_createPolicyHolderDto);
        var holderResponse = await Client.GetAsync("/api/policyholders");

        Assert.Equal(HttpStatusCode.OK, holderResponse.StatusCode);
        var holders = (await holderResponse.Content.ReadFromJsonAsync<PagedResult<PolicyHolderDto>>() ??
                       new PagedResult<PolicyHolderDto>()).Items;
        Assert.Single(holders);

        var holder = holders[0];
        Assert.Equal(_createPolicyHolderDto.FirstName, holder.FirstName);
        Assert.Equal(_createPolicyHolderDto.LastName, holder.LastName);
        Assert.Equal(_createPolicyHolderDto.Email, holder.Email);
    }

    /// <summary>
    ///     The list endpoint honours page, page size, sort and direction from the query string.
    /// </summary>
    [Fact]
    public async Task GetAll_PageSizeOne_SecondPageIsTheNextHolder()
    {
        await SeedHolderAsync(new CreatePolicyHolderDto { FirstName = "Ann", LastName = "Able", Email = "ann@example.com" });
        await SeedHolderAsync(new CreatePolicyHolderDto { FirstName = "Bob", LastName = "Baker", Email = "bob@example.com" });

        var response = await Client.GetAsync("/api/policyholders?page=2&pageSize=1&sortBy=lastName");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<PolicyHolderDto>>();
        Assert.NotNull(page);
        var holder = Assert.Single(page!.Items);
        Assert.Equal("Baker", holder.LastName);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasPrevious);
        Assert.False(page.HasNext);
    }

    /// <summary>
    ///     An out-of-range page size is clamped rather than rejected, and an unknown sort key falls
    ///     back to the default ordering instead of failing the request.
    /// </summary>
    [Fact]
    public async Task GetAll_OutOfRangePageSizeAndUnknownSort_AreClampedAndDefaulted()
    {
        await SeedHolderAsync(_createPolicyHolderDto);

        var response = await Client.GetAsync("/api/policyholders?pageSize=5000&sortBy=notAColumn");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<PolicyHolderDto>>();
        Assert.NotNull(page);
        Assert.Equal(PaginationQuery.MaxPageSize, page!.PageSize);
        Assert.Equal(1, page.Page);
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

    /// <summary>
    ///     Creating a holder with a taken email returns 409, on every provider.
    /// </summary>
    /// <remarks>
    ///     The in-memory provider enforces no unique index, so this 409 comes from the
    ///     service-level check; on SQL Server the unique index backstops the race.
    /// </remarks>
    [Fact]
    public async Task Create_DuplicateEmail_Returns409()
    {
        var first = await Client.PostAsJsonAsync("/api/policyholders", _createPolicyHolderDto);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await Client.PostAsJsonAsync("/api/policyholders", _createPolicyHolderDto);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    /// <summary>
    ///     Updating a holder applies the supplied fields and leaves the rest of the record intact.
    /// </summary>
    [Fact]
    public async Task Update_ValidHolder_ChangesOnlyTheSuppliedFields()
    {
        var id = await SeedHolderAsync(_createPolicyHolderDto);

        var res = await Client.PutAsJsonAsync($"/api/policyholders/{id}",
            new UpdatePolicyHolderDto { LastName = "Roe" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var holder = await Client.GetFromJsonAsync<PolicyHolderDto>($"/api/policyholders/{id}");
        Assert.Equal("Jane", holder!.FirstName);
        Assert.Equal("Roe", holder.LastName);
        Assert.Equal(_createPolicyHolderDto.Email, holder.Email);
    }

    /// <summary>
    ///     Updating a holder that does not exist returns 404 rather than a 200 for a write that
    ///     changed nothing.
    /// </summary>
    [Fact]
    public async Task Update_NonExistentHolder_Returns404()
    {
        var res = await Client.PutAsJsonAsync("/api/policyholders/99999",
            new UpdatePolicyHolderDto { LastName = "Roe" });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    ///     An update body with no fields at all is rejected instead of being silently accepted.
    /// </summary>
    [Fact]
    public async Task Update_WithNoFields_Returns400()
    {
        var id = await SeedHolderAsync(_createPolicyHolderDto);

        var res = await Client.PutAsJsonAsync($"/api/policyholders/{id}", new UpdatePolicyHolderDto());

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    ///     Taking another holder's email address returns 409.
    /// </summary>
    [Fact]
    public async Task Update_EmailHeldByAnotherHolder_Returns409()
    {
        await SeedHolderAsync(_createPolicyHolderDto);
        var other = await SeedHolderAsync(new CreatePolicyHolderDto
            { FirstName = "John", LastName = "Smith", Email = "john@example.com" });

        var res = await Client.PutAsJsonAsync($"/api/policyholders/{other}",
            new UpdatePolicyHolderDto { Email = _createPolicyHolderDto.Email });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    /// <summary>
    ///     Deleting a holder who owns nothing removes them.
    /// </summary>
    [Fact]
    public async Task Delete_UnattachedHolder_Returns200AndRemovesThem()
    {
        var id = await SeedHolderAsync(_createPolicyHolderDto);

        var res = await Client.DeleteAsync($"/api/policyholders/{id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var get = await Client.GetAsync($"/api/policyholders/{id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    /// <summary>
    ///     Deleting a holder that does not exist returns 404.
    /// </summary>
    [Fact]
    public async Task Delete_NonExistentHolder_Returns404()
    {
        var res = await Client.DeleteAsync("/api/policyholders/99999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    ///     Deleting a holder who still has policies is refused, and the policies survive.
    /// </summary>
    /// <remarks>
    ///     The claims filed against those policies are financial records carrying the adjudication
    ///     trail, so the endpoint refuses the delete rather than removing the holder's history with
    ///     them.
    /// </remarks>
    [Fact]
    public async Task Delete_HolderWithPolicies_Returns409AndKeepsThePolicies()
    {
        var holderId = await SeedHolderAsync(_createPolicyHolderDto);
        var policyId = await SeedPolicyAsync(holderId);

        var res = await Client.DeleteAsync($"/api/policyholders/{holderId}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        var get = await Client.GetAsync($"/api/policies/{policyId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }
}

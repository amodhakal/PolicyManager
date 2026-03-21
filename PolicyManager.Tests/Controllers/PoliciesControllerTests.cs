using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Tests.Controllers;

public class PoliciesControllerTests : IAsyncLifetime
{
    private readonly CreatePolicyHolderDto _createPolicyHolderDto =
        new() { FirstName = "Jane", LastName = "Doe", Email = "janedoe@gmail.com" };

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;

    public PoliciesControllerTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var toRemove = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                                    || d.ServiceType == typeof(DbContext)
                                    || d.ServiceType == typeof(AppDbContext)
                                    || (d.ImplementationType?.Name.Contains("AppDbContext") ?? false)
                                    || (d.ServiceType.FullName?.Contains("DbContextOptions") ?? false))
                        .ToList();

                    foreach (var d in toRemove) services.Remove(d);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseInMemoryDatabase(_dbName));
                });
            });
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<int> SeedHolderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var holder = new PolicyHolder
        {
            FirstName = _createPolicyHolderDto.FirstName,
            LastName = _createPolicyHolderDto.LastName,
            Email = _createPolicyHolderDto.Email
        };

        db.PolicyHolders.Add(holder);
        await db.SaveChangesAsync();
        return holder.Id;
    }

    private async Task<int> SeedPolicyAsync(int holderId, decimal premium = 500m)
    {
        var dto = new CreatePolicyDto { PolicyHolderId = holderId, Premium = premium };
        var res = await _client.PostAsJsonAsync("/api/policies", dto);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<int>();
    }

    /// <summary>
    ///     Creating a policy with valid data returns 200/201 and a positive integer ID.
    /// </summary>
    [Fact]
    public async Task CreatePolicy_ValidRequest_ReturnsId()
    {
        var holderId = await SeedHolderAsync();
        var dto = new CreatePolicyDto { PolicyHolderId = holderId, Premium = 750m };

        var res = await _client.PostAsJsonAsync("/api/policies", dto);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var id = await res.Content.ReadFromJsonAsync<int>();
        Assert.True(id > 0);
    }

    /// <summary>
    ///     Creating a policy actually persists it — GET by ID returns the same premium.
    /// </summary>
    [Fact]
    public async Task CreatePolicy_Persists_CanBeRetrieved()
    {
        var holderId = await SeedHolderAsync();
        var id = await SeedPolicyAsync(holderId, 999m);

        var res = await _client.GetAsync($"/api/policies/{id}");
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

        var res = await _client.GetAsync("/api/policies");
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

        await _client.DeleteAsync($"/api/policies/{cancelledId}");

        var res = await _client.GetAsync("/api/policies?status=Active");
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

        await _client.DeleteAsync($"/api/policies/{cancelledId}");

        var res = await _client.GetAsync("/api/policies?status=Cancelled");
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

        var res = await _client.GetAsync($"/api/policies/{id}");
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
        var res = await _client.GetAsync("/api/policies/99999");
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
        var res = await _client.PutAsJsonAsync($"/api/policies/{id}", updateDto);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var getRes = await _client.GetAsync($"/api/policies/{id}");
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

        var deleteRes = await _client.DeleteAsync($"/api/policies/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        var getRes = await _client.GetAsync($"/api/policies/{id}");
        var dto = await getRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.Equal(PolicyStatus.Cancelled, dto!.Status);
    }

    /// <summary>
    ///     Cancelling a non-existent policy does not return a 5xx — service swallows it.
    /// </summary>
    [Fact]
    public async Task Cancel_NonExistentId_DoesNotReturn5xx()
    {
        var res = await _client.DeleteAsync("/api/policies/99999");
        Assert.True((int)res.StatusCode < 500);
    }
}
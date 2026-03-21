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

public class ClaimsControllerTests : IAsyncLifetime
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;

    public ClaimsControllerTests()
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

        var holder = new PolicyHolder { FirstName = "Jane", LastName = "Doe", Email = "jd@gmail.com" };
        db.PolicyHolders.Add(holder);
        await db.SaveChangesAsync();
        return holder.Id;
    }

    private async Task<int> SeedPolicyAsync(int holderId)
    {
        var res = await _client.PostAsJsonAsync("/api/policies",
            new CreatePolicyDto { PolicyHolderId = holderId, Premium = 500m });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<int>();
    }

    private async Task<int> SeedClaimAsync(int policyId)
    {
        var dto = new ClaimDto { PolicyId = policyId, Amount = 200m };
        var res = await _client.PostAsJsonAsync("/api/claims", dto);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<int>();
    }

    /// <summary>
    ///     Creating a claim against an existing policy returns 201 and a positive ID.
    /// </summary>
    [Fact]
    public async Task CreateClaim_ValidPolicy_ReturnsCreated()
    {
        var holderId = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holderId);

        var res = await _client.PostAsJsonAsync("/api/claims",
            new ClaimDto { PolicyId = policyId, Amount = 300m });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var id = await res.Content.ReadFromJsonAsync<int>();
        Assert.True(id > 0);
    }

    /// <summary>
    ///     Creating a claim against a non-existent policy returns 400.
    /// </summary>
    [Fact]
    public async Task CreateClaim_NonExistentPolicy_ReturnsBadRequest()
    {
        var res = await _client.PostAsJsonAsync("/api/claims",
            new ClaimDto { PolicyId = 99999, Amount = 300m });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    ///     GetAll returns all seeded claims.
    /// </summary>
    [Fact]
    public async Task GetAll_ReturnsAllClaims()
    {
        var holderId = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holderId);
        await SeedClaimAsync(policyId);
        await SeedClaimAsync(policyId);

        var res = await _client.GetAsync("/api/claims");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var claims = await res.Content.ReadFromJsonAsync<List<ClaimDto>>();
        Assert.Equal(2, claims!.Count);
    }

    /// <summary>
    ///     GetAll on an empty database returns an empty list, not 404.
    /// </summary>
    [Fact]
    public async Task GetAll_NoClaims_ReturnsEmptyList()
    {
        var res = await _client.GetAsync("/api/claims");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var claims = await res.Content.ReadFromJsonAsync<List<ClaimDto>>();
        Assert.Empty(claims!);
    }

    /// <summary>
    ///     GetById for an existing claim returns 200 with correct policy reference.
    /// </summary>
    [Fact]
    public async Task GetById_ExistingId_ReturnsCorrectDto()
    {
        var holderId = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holderId);
        var claimId = await SeedClaimAsync(policyId);

        var res = await _client.GetAsync($"/api/claims/{claimId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var dto = await res.Content.ReadFromJsonAsync<ClaimDto>();
        Assert.NotNull(dto);
        Assert.Equal(policyId, dto!.PolicyId);
        Assert.Equal(200m, dto.Amount);
    }

    /// <summary>
    ///     GetById for a non-existent ID returns 404.
    /// </summary>
    [Fact]
    public async Task GetById_NonExistentId_Returns404()
    {
        var res = await _client.GetAsync("/api/claims/99999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    ///     Updating claim status returns 200 and the change is persisted.
    /// </summary>
    [Fact]
    public async Task UpdateStatus_ValidClaim_ReturnsOkAndPersists()
    {
        var holderId = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holderId);
        var claimId = await SeedClaimAsync(policyId);

        var patchRes = await _client.PatchAsJsonAsync($"/api/claims/{claimId}/status",
            new ClaimDto { PolicyId = policyId, Status = ClaimStatus.Approved });
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        var getRes = await _client.GetAsync($"/api/claims/{claimId}");
        var dto = await getRes.Content.ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(ClaimStatus.Approved, dto!.Status);
    }
}
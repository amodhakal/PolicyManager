using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;

namespace PolicyManager.Tests.Controllers;

public class PolicyHoldersControllerTests : IAsyncLifetime
{
    private readonly CreatePolicyHolderDto _createPolicyHolderDto =
        new() { FirstName = "Jane", LastName = "Doe", Email = "jd@gmail.com" };

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly WebApplicationFactory<Program> _factory;

    private HttpClient _client = null!;


    public PolicyHoldersControllerTests()
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
            FirstName = _createPolicyHolderDto.FirstName, LastName = _createPolicyHolderDto.LastName,
            Email = _createPolicyHolderDto.Email
        };

        db.PolicyHolders.Add(holder);
        await db.SaveChangesAsync();
        return holder.Id;
    }

    [Fact]
    public async Task Create_ValidHolder_Returns201WithId()
    {
        var response = await _client.PostAsJsonAsync("/api/policyholders", _createPolicyHolderDto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = await response.Content.ReadFromJsonAsync<int>();
        Assert.True(id > 0);
    }

    [Fact]
    public async Task GetAll()
    {
        var emptyHolderResponse = await _client.GetAsync("/api/policyholders");

        Assert.Equal(HttpStatusCode.OK, emptyHolderResponse.StatusCode);
        var emptyHolders = await emptyHolderResponse.Content.ReadFromJsonAsync<IEnumerable<PolicyHolderDto>>();
        Assert.NotNull(emptyHolders);
        Assert.Empty(emptyHolders);


        await SeedHolderAsync();
        var holderResponse = await _client.GetAsync("/api/policyholders");

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
        var id = await SeedHolderAsync();
        var response = await _client.GetAsync($"/api/policyholders/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var holder = await response.Content.ReadFromJsonAsync<PolicyHolderDto>();
        Assert.NotNull(holder);
        Assert.Equal(_createPolicyHolderDto.FirstName, holder.FirstName);
        Assert.Equal(_createPolicyHolderDto.Email, holder.Email);
    }

    [Fact]
    public async Task GetById_NonExistentHolder_Returns404()
    {
        var response = await _client.GetAsync("/api/policyholders/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
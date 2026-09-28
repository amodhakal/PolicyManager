using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;

namespace PolicyManager.Tests.Infrastructure;

/// <summary>
///     Base class for controller (integration) tests.
/// </summary>
/// <remarks>
///     Owns the in-memory <see cref="AppDbContext" /> test host, the <see cref="HttpClient" />, and the
///     holder/policy/claim seeding chain that the controller test classes all need.
/// </remarks>
public abstract class ApiIntegrationTestBase : IAsyncLifetime
{
    private readonly InMemoryApiFactory _factory = new();

    /// <summary>
    ///     Gets the client used to talk to the test server.
    /// </summary>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    ///     Gets the policyholder used by <see cref="SeedHolderAsync" /> when the test does not
    ///     supply one of its own. Override to seed a different holder.
    /// </summary>
    protected virtual CreatePolicyHolderDto DefaultHolder =>
        new() { FirstName = "Jane", LastName = "Doe", Email = "jd@gmail.com" };

    /// <summary>
    ///     Adjusts the test host before the client is built. Override to override configuration or
    ///     replace a service for one test class; the database swap is already applied and runs
    ///     before this.
    /// </summary>
    /// <param name="builder">The web host builder for the test server.</param>
    protected virtual void ConfigureTestHost(IWebHostBuilder builder)
    {
    }

    /// <summary>
    ///     Creates the client and resets the in-memory database for the test.
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        _factory.ConfigureHost = ConfigureTestHost;
        Client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    ///     Disposes the client and the test host.
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>
    ///     Reads the identifier out of a 201 response body, which carries the created resource.
    /// </summary>
    /// <param name="response">The response returned by a create endpoint.</param>
    /// <param name="idSelector">Selects the identifier from the deserialized DTO.</param>
    /// <typeparam name="TDto">The DTO returned by the create endpoint.</typeparam>
    /// <returns>The identifier of the created resource.</returns>
    protected static async Task<int> ReadCreatedIdAsync<TDto>(HttpResponseMessage response, Func<TDto, int> idSelector)
        where TDto : class
    {
        var dto = await response.Content.ReadFromJsonAsync<TDto>();
        Assert.NotNull(dto);
        return idSelector(dto!);
    }

    /// <summary>
    ///     Inserts a policyholder directly into the database, bypassing the API.
    /// </summary>
    /// <param name="holder">The holder to insert; <see cref="DefaultHolder" /> is used when omitted.</param>
    /// <returns>The ID of the created policyholder.</returns>
    protected async Task<int> SeedHolderAsync(CreatePolicyHolderDto? holder = null)
    {
        holder ??= DefaultHolder;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entity = new PolicyHolder
        {
            FirstName = holder.FirstName, LastName = holder.LastName,
            Email = holder.Email
        };

        db.PolicyHolders.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    /// <summary>
    ///     Creates a policy through the API.
    /// </summary>
    /// <param name="holderId">The owning policyholder's ID.</param>
    /// <param name="premium">The policy premium.</param>
    /// <returns>The ID of the created policy.</returns>
    protected async Task<int> SeedPolicyAsync(int holderId, decimal premium = 500m)
    {
        var res = await Client.PostAsJsonAsync("/api/policies",
            new CreatePolicyDto { Type = Models.Enums.PolicyType.Auto, PolicyHolderId = holderId, Premium = premium, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1) });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await ReadCreatedIdAsync<PolicyDto>(res, p => p.Id);
    }

    /// <summary>
    ///     Creates a policyholder and then a policy for it.
    /// </summary>
    /// <param name="premium">The policy premium.</param>
    /// <returns>The ID of the created policy.</returns>
    protected async Task<int> SeedPolicyForNewHolderAsync(decimal premium = 500m)
    {
        var holderId = await SeedHolderAsync();
        return await SeedPolicyAsync(holderId, premium);
    }

    /// <summary>
    ///     Creates a claim through the API.
    /// </summary>
    /// <param name="policyId">The policy the claim is filed against.</param>
    /// <param name="amount">The claim amount.</param>
    /// <returns>The ID of the created claim.</returns>
    protected async Task<int> SeedClaimAsync(int policyId, decimal amount = 200m)
    {
        var res = await Client.PostAsJsonAsync("/api/claims",
            new CreateClaimDto { PolicyId = policyId, Amount = amount });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await ReadCreatedIdAsync<ClaimDto>(res, c => c.Id);
    }

    /// <summary>
    ///     Creates a policyholder, a policy for it, and a claim against that policy.
    /// </summary>
    /// <param name="amount">The claim amount.</param>
    /// <returns>The ID of the created claim.</returns>
    protected async Task<int> SeedClaimForNewPolicyAsync(decimal amount = 200m)
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        return await SeedClaimAsync(policyId, amount);
    }
}

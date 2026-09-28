using Microsoft.Extensions.Caching.Memory;
using PolicyManager.DTOs;
using PolicyManager.Exceptions;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Services;

/// <summary>
///     Unit tests for the PolicyHoldersService class.
/// </summary>
public class PolicyHoldersServiceTests : ServiceTestBase
{
    private readonly IMemoryCache _cache;
    private readonly PolicyHoldersService _policyHoldersService;

    public PolicyHoldersServiceTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _policyHoldersService = new PolicyHoldersService(Context, _cache);
    }

    /// <summary>
    ///     Seeds a test policyholder into the database.
    /// </summary>
    /// <param name="first">The first name.</param>
    /// <param name="last">The last name.</param>
    /// <param name="email">The email address.</param>
    /// <returns>The ID of the created policyholder.</returns>
    private async Task<int> SeedHolder(string first = "Jane", string last = "Doe", string email = "jane@example.com")
    {
        return await _policyHoldersService.Create(new CreatePolicyHolderDto
        {
            FirstName = first,
            LastName = last,
            Email = email
        });
    }

    /// <summary>
    ///     Verifies that creating a policyholder persists it and returns the ID.
    /// </summary>
    [Fact]
    public async Task Create_PersistsHolder_ReturnsId()
    {
        var id = await SeedHolder("John", "Smith", "js@test.com");
        var holder = await Context.PolicyHolders.FindAsync(id);

        Assert.NotNull(holder);
        Assert.Equal("John", holder.FirstName);
        Assert.Equal("js@test.com", holder.Email);
    }

    /// <summary>
    ///     Verifies that GetAll returns every policy holder when they all fit on the first page.
    /// </summary>
    [Fact]
    public async Task GetAll_ReturnsAllHolders()
    {
        await SeedHolder("A", "A", "a@a.com");
        await SeedHolder("B", "B", "b@b.com");

        var result = await _policyHoldersService.GetAll(new PaginationQuery());
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.False(result.HasNext);
    }

    /// <summary>
    ///     Verifies that GetById returns the correct DTO.
    /// </summary>
    [Fact]
    public async Task GetById_ReturnsCorrectDto()
    {
        var id = await SeedHolder("Sue", "Storm", "sue@ff.com");
        var result = await _policyHoldersService.GetById(id);

        Assert.NotNull(result);
        Assert.Equal("Sue", result!.FirstName);
        Assert.Equal("Storm", result.LastName);
        Assert.Equal("sue@ff.com", result.Email);
    }

    /// <summary>
    ///     Verifies that GetById returns null for non-existent ID.
    /// </summary>
    [Fact]
    public async Task GetById_NotFound_ReturnsNull()
    {
        var result = await _policyHoldersService.GetById(99999);
        Assert.Null(result);
    }

    /// <summary>
    ///     Verifies that creating a holder with a taken email raises ConflictException.
    /// </summary>
    /// <remarks>
    ///     The in-memory provider enforces no unique index, so without the service-level
    ///     check this insert would succeed and the duplicate would only fail on SQL Server.
    /// </remarks>
    [Fact]
    public async Task Create_DuplicateEmail_ThrowsConflict()
    {
        await SeedHolder("Jane", "Doe", "jane@example.com");

        await Assert.ThrowsAsync<ConflictException>(() =>
            _policyHoldersService.Create(new CreatePolicyHolderDto
                { FirstName = "Janet", LastName = "Roe", Email = "jane@example.com" }));
    }

    /// <summary>
    ///     Releases the cache in addition to the base resources.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing) _cache.Dispose();
        base.Dispose(disposing);
    }
}

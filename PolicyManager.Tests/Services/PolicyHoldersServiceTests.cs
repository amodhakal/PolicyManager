using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PolicyManager.DTOs;
using PolicyManager.Exceptions;
using PolicyManager.Models;
using PolicyManager.Models.Enums;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Services;

/// <summary>
///     Unit tests for the PolicyHoldersService class.
/// </summary>
public class PolicyHoldersServiceTests : ServiceTestBase
{
    private readonly IMemoryCache _cache;
    private readonly PolicyHolderWriteGenerations _generations;
    private readonly PolicyHoldersService _policyHoldersService;

    public PolicyHoldersServiceTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _generations = new PolicyHolderWriteGenerations();
        _policyHoldersService = new PolicyHoldersService(Context, _cache, _generations);
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
    ///     Verifies that an update applies the fields it was given and leaves the others alone.
    /// </summary>
    [Fact]
    public async Task Update_AppliesSuppliedFieldsAndLeavesTheRest()
    {
        var id = await SeedHolder("Jane", "Doe", "jane@example.com");

        await _policyHoldersService.Update(id, new UpdatePolicyHolderDto { LastName = "Roe" });

        var holder = await Context.PolicyHolders.AsNoTracking().SingleAsync(h => h.Id == id);
        Assert.Equal("Jane", holder.FirstName);
        Assert.Equal("Roe", holder.LastName);
        Assert.Equal("jane@example.com", holder.Email);
    }

    /// <summary>
    ///     Verifies that updating a holder that does not exist raises NotFoundException.
    /// </summary>
    [Fact]
    public async Task Update_NonExistentId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _policyHoldersService.Update(99999, new UpdatePolicyHolderDto { LastName = "Roe" }));
    }

    /// <summary>
    ///     Verifies that taking another holder's email on update raises ConflictException, and that
    ///     re-submitting a holder's own address is not a conflict with itself.
    /// </summary>
    [Fact]
    public async Task Update_EmailAlreadyHeldByAnotherHolder_ThrowsConflict()
    {
        await SeedHolder("Jane", "Doe", "jane@example.com");
        var otherId = await SeedHolder("John", "Smith", "john@example.com");

        await Assert.ThrowsAsync<ConflictException>(() =>
            _policyHoldersService.Update(otherId,
                new UpdatePolicyHolderDto { Email = "jane@example.com" }));

        await _policyHoldersService.Update(otherId,
            new UpdatePolicyHolderDto { FirstName = "Johnny", Email = "john@example.com" });

        Assert.Equal("Johnny", (await Context.PolicyHolders.AsNoTracking().SingleAsync(h => h.Id == otherId)).FirstName);
    }

    /// <summary>
    ///     Verifies that an update and the delete record their outbox messages.
    /// </summary>
    [Fact]
    public async Task Update_AndDelete_WriteOutboxMessages()
    {
        var id = await SeedHolder("Jane", "Doe", "jane@example.com");

        await _policyHoldersService.Update(id, new UpdatePolicyHolderDto { LastName = "Roe" });
        await _policyHoldersService.Delete(id);

        var types = await Context.OutboxMessages.Select(m => m.Type).ToListAsync();
        Assert.Contains("PolicyHolderUpdated", types);
        Assert.Contains("PolicyHolderDeleted", types);
    }

    /// <summary>
    ///     Verifies that deleting an unattached holder removes it.
    /// </summary>
    [Fact]
    public async Task Delete_UnattachedHolder_RemovesTheRow()
    {
        var id = await SeedHolder("Jane", "Doe", "jane@example.com");

        await _policyHoldersService.Delete(id);

        Assert.Empty(await Context.PolicyHolders.ToListAsync());
    }

    /// <summary>
    ///     Verifies that deleting a holder that does not exist raises NotFoundException.
    /// </summary>
    [Fact]
    public async Task Delete_NonExistentId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _policyHoldersService.Delete(99999));
    }

    /// <summary>
    ///     Verifies that a holder who still owns policies is refused, and that the policies are kept.
    /// </summary>
    /// <remarks>
    ///     The alternative is the cascade the foreign keys now prevent: the policies, and every claim
    ///     filed under them, would go with the holder as a side effect of removing a contact record.
    /// </remarks>
    [Fact]
    public async Task Delete_HolderStillOwningPolicies_ThrowsConflictAndKeepsEverything()
    {
        var holderId = await SeedHolder("Jane", "Doe", "jane@example.com");
        var policiesService = new PoliciesService(Context);
        var policyId = await policiesService.Create(new CreatePolicyDto
        {
            Type = PolicyType.Auto, PolicyHolderId = holderId, Premium = 500m,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        });

        await Assert.ThrowsAsync<ConflictException>(() => _policyHoldersService.Delete(holderId));

        Assert.Single(await Context.PolicyHolders.ToListAsync());
        Assert.Single(await Context.Policies.ToListAsync());
        Assert.Equal(policyId, (await Context.Policies.SingleAsync()).Id);
    }

    /// <summary>
    ///     A read that began before an update and finishes after it cannot resurrect the old row.
    /// </summary>
    /// <remarks>
    ///     The cache is the whole point of this test: without the write generation in the key, the
    ///     read stores the pre-update row under a key the update's eviction has already passed, and
    ///     every subsequent reader is served that row until the entry expires - an update that reports
    ///     success and is then invisible.
    /// </remarks>
    [Fact]
    public async Task Update_IsNotMaskedByAReadThatWasAlreadyInFlight()
    {
        var id = await SeedHolder("Jane", "Doe", "jane@example.com");

        // The read that is "in flight": it took the generation and the row, then lost the thread.
        var generationBeforeUpdate = _generations.Current(id);
        var rowReadBeforeUpdate = await _policyHoldersService.GetById(id);

        await _policyHoldersService.Update(id, new UpdatePolicyHolderDto { LastName = "Roe" });

        // It finishes now, caching what it read before the write.
        _cache.Set(CacheKeys.ById(id, generationBeforeUpdate), rowReadBeforeUpdate);

        var reread = await _policyHoldersService.GetById(id);

        Assert.Equal("Roe", reread!.LastName);
    }

    /// <summary>
    ///     A read that began before a delete and finishes after it cannot resurrect the deleted row.
    /// </summary>
    /// <remarks>
    ///     The same race as the update, and the more damaging half: the stale entry would answer
    ///     <c>GET</c> with a holder that no longer exists, and the caller would go on to act on it.
    /// </remarks>
    [Fact]
    public async Task Delete_IsNotMaskedByAReadThatWasAlreadyInFlight()
    {
        var id = await SeedHolder("Jane", "Doe", "jane@example.com");

        var generationBeforeDelete = _generations.Current(id);
        var rowReadBeforeDelete = await _policyHoldersService.GetById(id);

        await _policyHoldersService.Delete(id);

        _cache.Set(CacheKeys.ById(id, generationBeforeDelete), rowReadBeforeDelete);

        Assert.Null(await _policyHoldersService.GetById(id));
    }

    /// <summary>
    ///     Two updates in a row each retire the previous generation, so the value read between them
    ///     cannot come back.
    /// </summary>
    [Fact]
    public async Task ConsecutiveUpdates_EachRetireThePreviousGeneration()
    {
        var id = await SeedHolder("Jane", "Doe", "jane@example.com");
        var firstGeneration = _generations.Current(id);

        await _policyHoldersService.Update(id, new UpdatePolicyHolderDto { LastName = "Roe" });
        var middle = await _policyHoldersService.GetById(id);

        await _policyHoldersService.Update(id, new UpdatePolicyHolderDto { LastName = "Smith" });

        // The value read between the two writes arrives late.
        _cache.Set(CacheKeys.ById(id, firstGeneration + 1), middle);

        var reread = await _policyHoldersService.GetById(id);

        Assert.Equal("Smith", reread!.LastName);
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

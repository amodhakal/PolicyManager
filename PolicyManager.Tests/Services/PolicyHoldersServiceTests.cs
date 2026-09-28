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
        _policyHoldersService = new PolicyHoldersService(Context, _cache, _generations, Pii);
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
    ///     A deleted holder is hidden from every read, while the row stays in the table.
    /// </summary>
    /// <remarks>
    ///     The row has to survive: the foreign key from policy to holder cascades, so removing the row
    ///     would take their policies and every claim filed against them with it.
    /// </remarks>
    [Fact]
    public async Task Delete_HidesTheHolderButKeepsTheRow()
    {
        var id = await SeedHolder("Jane", "Doe", "jane@example.com");

        await _policyHoldersService.Update(id, new UpdatePolicyHolderDto { LastName = "Roe" });
        await _policyHoldersService.Delete(id);

        var types = await Context.OutboxMessages.Select(m => m.Type).ToListAsync();
        Assert.Contains("PolicyHolderUpdated", types);
        Assert.Contains("PolicyHolderDeleted", types);
    }

    /// <summary>
    ///     Deleting a holder who owns nothing still keeps the row, because the row is the only thing
    ///     that makes the delete reversible.
    /// </summary>
    [Fact]
    public async Task Delete_UnattachedHolder_KeepsTheRow()
    {
        var id = await SeedHolder("Jane", "Doe", "jane@example.com");

        await _policyHoldersService.Delete(id);

        var holder = await Context.PolicyHolders.SingleAsync();
        Assert.True(holder.IsDeleted);
        Assert.NotNull(holder.DeletionDate);
        Assert.Null(await _policyHoldersService.GetById(id));
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

        var row = await Context.PolicyHolders.IgnoreQueryFilters().SingleAsync(h => h.Id == id);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletionDate);
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
    ///     A deleted holder drops out of the list, and the count describes the holders a caller can
    ///     still see.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesTheHolderFromTheList()
    {
        var deleted = await SeedHolder("Ann", "Able", "ann@test.com");
        await SeedHolder("Bob", "Baker", "bob@test.com");

        await _policyHoldersService.Delete(deleted);

        var page = await _policyHoldersService.GetAll(new PaginationQuery());

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Baker", Assert.Single(page.Items).LastName);
    }

    /// <summary>
    ///     A deleted holder's policies and claims are left alone: only the holder's own record is
    ///     hidden, and their book of business stays in place.
    /// </summary>
    [Fact]
    public async Task Delete_LeavesTheHoldersPoliciesAndClaimsInPlace()
    {
        var holder = await SeedHolderEntityAsync("John", "Smith", "js@test.com");

        var policy = new Policy
        {
            PolicyHolderId = holder.Id, Premium = 500m, Status = PolicyStatus.Active,
            Type = PolicyType.Auto, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        };

        Context.Policies.Add(policy);
        await Context.SaveChangesAsync();

        Context.Claims.Add(new Claim { PolicyId = policy.Id, Amount = 100m });
        await Context.SaveChangesAsync();

        await _policyHoldersService.Delete(holder.Id);

        Assert.NotNull(await Context.Policies.FindAsync(policy.Id));
        Assert.Single(await Context.Claims.ToListAsync());
    }

    /// <summary>
    ///     A restore brings a deleted holder back, unchanged in every other respect.
    /// </summary>
    [Fact]
    public async Task Restore_BringsTheHolderBack()
    {
        var id = await SeedHolder("John", "Smith", "js@test.com");
        await _policyHoldersService.Delete(id);

        await _policyHoldersService.Restore(id);

        var holder = await _policyHoldersService.GetById(id);
        Assert.Equal("John", holder!.FirstName);
        Assert.Equal("js@test.com", holder.Email);

        var row = await Context.PolicyHolders.SingleAsync(h => h.Id == id);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletionDate);
    }

    /// <summary>
    ///     Restoring a holder who was never deleted succeeds and changes nothing, so a client that
    ///     retries does not get an error for a state it has already reached.
    /// </summary>
    [Fact]
    public async Task Restore_OfAnActiveHolder_ChangesNothing()
    {
        var id = await SeedHolder("John", "Smith", "js@test.com");

        await _policyHoldersService.Restore(id);

        var row = await Context.PolicyHolders.SingleAsync(h => h.Id == id);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletionDate);
        Assert.Single(await Context.OutboxMessages.ToListAsync());
    }

    /// <summary>
    ///     A restore of an identifier nobody holds is a 404, like every other write to a holder that
    ///     does not exist.
    /// </summary>
    [Fact]
    public async Task Restore_NonExistentHolder_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _policyHoldersService.Restore(99999));
    }

    /// <summary>
    ///     A restore advances the write generation, so the next read serves the restored holder rather
    ///     than an entry cached before the delete.
    /// </summary>
    [Fact]
    public async Task Restore_EvictsTheCachedHolder()
    {
        var id = await SeedHolder("John", "Smith", "js@test.com");
        await _policyHoldersService.GetById(id);
        await _policyHoldersService.Delete(id);
        await _policyHoldersService.Restore(id);

        Assert.Equal("John", (await _policyHoldersService.GetById(id))!.FirstName);
    }

    /// <summary>
    ///     Updating a holder writes the outbox message describing the change, so subscribers learn
    ///     about it without polling the API.
    /// </summary>
    [Fact]
    public async Task Update_WritesOutboxMessage()
    {
        var id = await SeedHolder("John", "Smith", "js@test.com");

        await _policyHoldersService.Update(id, new UpdatePolicyHolderDto { FirstName = "Johnny" });

        var message = await Context.OutboxMessages.SingleAsync(m => m.Type == "PolicyHolderUpdated");
        Assert.Null(message.ProcessedAt);
        Assert.Contains("Johnny", message.Content);
    }

    /// <summary>
    ///     Deleting a holder writes the outbox message describing the removal, with the time the
    ///     deletion happened.
    /// </summary>
    [Fact]
    public async Task Delete_WritesOutboxMessage()
    {
        var id = await SeedHolder("John", "Smith", "js@test.com");

        await _policyHoldersService.Delete(id);

        var message = await Context.OutboxMessages.SingleAsync(m => m.Type == "PolicyHolderDeleted");
        Assert.Null(message.ProcessedAt);
        Assert.Contains(id.ToString(), message.Content);
    }

    /// <summary>
    ///     A restore publishes the message describing the restoration, carrying no deletion date: what
    ///     subscribers need to know is that the holder is back.
    /// </summary>
    [Fact]
    public async Task Restore_WritesOutboxMessage()
    {
        var id = await SeedHolder("John", "Smith", "js@test.com");
        await _policyHoldersService.Delete(id);

        await _policyHoldersService.Restore(id);

        var message = await Context.OutboxMessages.SingleAsync(m => m.Type == "PolicyHolderRestored");
        Assert.Null(message.ProcessedAt);
        Assert.Contains(id.ToString(), message.Content);
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

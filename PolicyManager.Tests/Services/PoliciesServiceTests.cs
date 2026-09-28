using Microsoft.EntityFrameworkCore;
using PolicyManager.DTOs;
using PolicyManager.Exceptions;
using PolicyManager.Models.Enums;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Services;

/// <summary>
///     Unit tests for the PoliciesService class.
/// </summary>
public class PoliciesServiceTests : ServiceTestBase
{
    private readonly PoliciesService _policiesService;

    public PoliciesServiceTests()
    {
        _policiesService = new PoliciesService(Context, Numbers);
    }

    /// <summary>
    ///     Seeds a test policy into the database.
    /// </summary>
    /// <param name="holderId">The policyholder ID.</param>
    /// <param name="premium">The policy premium.</param>
    /// <param name="status">The initial policy status.</param>
    /// <returns>The ID of the created policy.</returns>
    private async Task<int> SeedPolicy(
        int holderId, decimal premium = 500m, PolicyStatus status = PolicyStatus.Active)
    {
        return await _policiesService.Create(new CreatePolicyDto { Type = Models.Enums.PolicyType.Auto, PolicyHolderId = holderId, Premium = premium, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1) });
    }

    /// <summary>
    ///     Verifies that creating a policy returns a new ID and persists it.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsNewId_AndPersists()
    {
        var holder = await SeedHolderEntityAsync();
        var dto = new CreatePolicyDto { Type = Models.Enums.PolicyType.Auto, PolicyHolderId = holder.Id, Premium = 750m, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1) };

        var id = await _policiesService.Create(dto);
        var saved = await Context.Policies.FindAsync(id);
        Assert.NotNull(saved);
        Assert.Equal(750m, saved.Premium);
        Assert.Equal(PolicyStatus.Active, saved.Status);
    }

    /// <summary>
    ///     Verifies that cancelling a policy sets its status to Canceled but row still exists.
    /// </summary>
    [Fact]
    public async Task Cancel_SetsCancelledStatus_RowStillExists()
    {
        var holder = await SeedHolderEntityAsync();
        var id = await SeedPolicy(holder.Id);
        await _policiesService.Cancel(id);

        var policy = await Context.Policies.FindAsync(id);
        Assert.NotNull(policy);
        Assert.Equal(PolicyStatus.Cancelled, policy.Status);
    }

    /// <summary>
    ///     Verifies that cancelling a non-existent policy raises NotFoundException.
    /// </summary>
    /// <remarks>
    ///     This asserted the opposite: that the call completed silently. A silent completion is
    ///     indistinguishable at the call site from a successful write, which is how a missing policy
    ///     came to be reported to API callers as a 200.
    /// </remarks>
    [Fact]
    public async Task Cancel_NonExistentId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _policiesService.Cancel(99999));
    }

    /// <summary>
    ///     Verifies that updating a non-existent policy raises NotFoundException.
    /// </summary>
    [Fact]
    public async Task Update_NonExistentId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _policiesService.Update(99999, new UpdatePolicyDto { Premium = 100m }));
    }

    /// <summary>
    ///     Verifies that creating a policy for a holder that does not exist raises NotFoundException.
    /// </summary>
    /// <remarks>
    ///     Without the check the insert reached the foreign key, which the in-memory provider does
    ///     not enforce and SQL Server reports as a constraint violation - so an unknown holder was a
    ///     500 on one provider and a 409 on the other, for the same request.
    /// </remarks>
    [Fact]
    public async Task Create_NonExistentHolder_ThrowsNotFound()
    {
        var dto = new CreatePolicyDto { Type = Models.Enums.PolicyType.Auto, PolicyHolderId = 99999, Premium = 500m, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1) };

        await Assert.ThrowsAsync<NotFoundException>(() => _policiesService.Create(dto));

        Assert.Empty(await Context.Policies.ToListAsync());
    }

    /// <summary>
    ///     Verifies that GetAll with status filter returns only matching policies.
    /// </summary>
    [Fact]
    public async Task GetAll_FilterByStatus_ReturnsOnlyMatching()
    {
        var holder = await SeedHolderEntityAsync();
        var activeId = await SeedPolicy(holder.Id);
        var cancelledId = await SeedPolicy(holder.Id);
        await _policiesService.Cancel(cancelledId);

        var active = (await _policiesService.GetAll(new PaginationQuery(), PolicyStatus.Active)).Items;

        Assert.Single(active);
        Assert.Equal(activeId, active[0].Id);
    }

    /// <summary>
    ///     Verifies that GetAll without filter returns all policies.
    /// </summary>
    [Fact]
    public async Task GetAll_NoFilter_ReturnsAll()
    {
        var holder = await SeedHolderEntityAsync();
        await SeedPolicy(holder.Id);
        await SeedPolicy(holder.Id);

        var all = await _policiesService.GetAll(new PaginationQuery(), null);
        Assert.Equal(2, all.Items.Count);
    }

    /// <summary>
    ///     Verifies that updating a policy changes both premium and status.
    /// </summary>
    [Fact]
    public async Task Update_ChangesPremiumAndStatus()
    {
        var holder = await SeedHolderEntityAsync();
        var id = await SeedPolicy(holder.Id);

        await _policiesService.Update(id, new UpdatePolicyDto { Premium = 999m, Status = PolicyStatus.Expired });

        var policy = await Context.Policies.FindAsync(id);
        Assert.Equal(999m, policy!.Premium);
        Assert.Equal(PolicyStatus.Expired, policy.Status);
    }

    /// <summary>
    ///     Verifies that GetById returns correct DTO including holder name.
    /// </summary>
    [Fact]
    public async Task GetById_ReturnsCorrectDto_WithHolderName()
    {
        var holder = await SeedHolderEntityAsync();
        var id = await SeedPolicy(holder.Id);
        var dto = await _policiesService.GetById(id);

        Assert.NotNull(dto);
        Assert.Equal("Jane Doe", dto!.PolicyholderName);
    }

    /// <summary>
    ///     Verifies that GetById returns null for non-existent ID.
    /// </summary>
    [Fact]
    public async Task GetById_NotFound_ReturnsNull()
    {
        var result = await _policiesService.GetById(99999);
        Assert.Null(result);
    }

    /// <summary>
    ///     Verifies that creating a policy transactional writes an outbox message.
    /// </summary>
    [Fact]
    public async Task Create_WritesOutboxMessage()
    {
        var holder = await SeedHolderEntityAsync();
        var dto = new CreatePolicyDto { Type = Models.Enums.PolicyType.Auto, PolicyHolderId = holder.Id, Premium = 1000m, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1) };

        await _policiesService.Create(dto);

        var outboxMessage = await Context.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "PolicyCreated");
        Assert.NotNull(outboxMessage);
        Assert.Null(outboxMessage.ProcessedAt);
        Assert.Contains("1000", outboxMessage.Content);
    }

    /// <summary>
    ///     A holder's policy search returns only that holder's policies, never anyone else's.
    /// </summary>
    [Fact]
    public async Task GetByPolicyHolder_ReturnsOnlyThatHoldersPolicies()
    {
        var jane = await SeedHolderEntityAsync("Jane", "Doe", "jane@example.com");
        var john = await SeedHolderEntityAsync("John", "Smith", "john@example.com");

        await SeedPolicy(jane.Id);
        await SeedPolicy(jane.Id);
        await SeedPolicy(john.Id);

        var result = await _policiesService.GetByPolicyHolder(jane.Id, new PaginationQuery());

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, p => Assert.Equal(jane.Id, p.PolicyHolderId));
    }

    /// <summary>
    ///     A holder with no policies gets an empty page, which is not the same answer as a holder who
    ///     does not exist.
    /// </summary>
    [Fact]
    public async Task GetByPolicyHolder_NoPolicies_ReturnsAnEmptyPage()
    {
        var jane = await SeedHolderEntityAsync("Jane", "Doe", "jane@example.com");

        var result = await _policiesService.GetByPolicyHolder(jane.Id, new PaginationQuery());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    /// <summary>
    ///     Searching for a holder nobody holds is a not-found rather than an empty page.
    /// </summary>
    [Fact]
    public async Task GetByPolicyHolder_NonExistentHolder_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _policiesService.GetByPolicyHolder(99999, new PaginationQuery()));
    }

    /// <summary>
    ///     The status filter narrows a holder's policies, and the count describes the filtered set.
    /// </summary>
    [Fact]
    public async Task GetByPolicyHolder_StatusFilter_CountsOnlyTheFilteredSet()
    {
        var holder = await SeedHolderEntityAsync();
        await SeedPolicy(holder.Id);
        var cancelled = await SeedPolicy(holder.Id);
        await SeedPolicy(holder.Id);

        await _policiesService.Cancel(cancelled);

        var result = await _policiesService.GetByPolicyHolder(
            holder.Id, new PaginationQuery(), PolicyStatus.Active);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, p => Assert.Equal(PolicyStatus.Active, p.Status));
    }

    /// <summary>
    ///     The search pages the holder's own policies: the total and the page slice are both scoped to
    ///     them, not to the whole table.
    /// </summary>
    [Fact]
    public async Task GetByPolicyHolder_PagesTheHoldersOwnPolicies()
    {
        var jane = await SeedHolderEntityAsync("Jane", "Doe", "jane@example.com");
        var john = await SeedHolderEntityAsync("John", "Smith", "john@example.com");

        var first = await SeedPolicy(jane.Id, 100m);
        var second = await SeedPolicy(jane.Id, 200m);
        // Belongs to someone else and must not appear in either total.
        await SeedPolicy(john.Id, 300m);

        var result = await _policiesService.GetByPolicyHolder(
            jane.Id, new PaginationQuery { SortBy = "premium", PageSize = 1 });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        var policy = Assert.Single(result.Items);
        Assert.Equal(first, policy.Id);
        Assert.Equal(100m, policy.Premium);

        result.Page = 2;
        var secondPage = await _policiesService.GetByPolicyHolder(
            jane.Id, new PaginationQuery { SortBy = "premium", PageSize = 1, Page = 2 });

        Assert.Equal(second, Assert.Single(secondPage.Items).Id);
    }
}

using Microsoft.Extensions.Caching.Memory;
using Moq;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Services;

/// <summary>
///     Tests for the paging, clamping and sorting behaviour shared by the three list services.
/// </summary>
/// <remarks>
///     The services are built on a real in-memory <see cref="PolicyManager.Data.AppDbContext" /> rather
///     than a mocked queryable, because the behaviour under test is what EF does with <c>Skip</c>,
///     <c>Take</c> and <c>OrderBy</c> — a mocked <c>IQueryable</c> would only prove the service called
///     them. The memory cache is a <see cref="Mock{T}" /> because the list path must not consult it at
///     all, and a mock turns that into an assertion instead of an observation.
/// </remarks>
public class PaginationTests : ServiceTestBase
{
    private readonly Mock<IMemoryCache> _cache;
    private readonly PolicyHoldersService _policyHoldersService;
    private readonly PoliciesService _policiesService;
    private readonly ClaimsService _claimsService;
    private int _holderCounter;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PaginationTests" /> class.
    /// </summary>
    public PaginationTests()
    {
        _cache = new Mock<IMemoryCache>();
        _policyHoldersService = new PolicyHoldersService(Context, _cache.Object, new PolicyHolderWriteGenerations(), Pii);
        _policiesService = new PoliciesService(Context, Numbers);
        _claimsService = new ClaimsService(Context, Numbers);
    }

    /// <summary>
    ///     Inserts policyholders whose identifiers ascend while their last names descend, so the
    ///     default ordering and a last-name ordering are exact opposites. A sorting test that
    ///     accidentally exercised the default path would then fail rather than pass.
    /// </summary>
    /// <param name="count">How many policyholders to insert.</param>
    private async Task SeedHoldersAsync(int count)
    {
        for (var i = 0; i < count; i++)
            Context.PolicyHolders.Add(new PolicyHolder
            {
                FirstName = $"First{i:00}",
                LastName = $"Last{count - 1 - i:00}",
                Email = $"holder{i:00}@example.com"
            });

        await Context.SaveChangesAsync();
    }

    /// <summary>
    ///     Inserts a policyholder with a known name, in the order the test states.
    /// </summary>
    /// <param name="first">The first name.</param>
    /// <param name="last">The last name.</param>
    private async Task SeedNamedHolderAsync(string first, string last)
    {
        Context.PolicyHolders.Add(new PolicyHolder
        {
            FirstName = first, LastName = last, Email = $"{first}.{last}@example.com"
        });

        await Context.SaveChangesAsync();
    }

    /// <summary>
    ///     Inserts a policyholder and a policy for it, so the row is in the same state the API would
    ///     create it in.
    /// </summary>
    /// <param name="premium">The policy premium.</param>
    /// <returns>The identifier of the created policy.</returns>
    private async Task<int> SeedPolicyAsync(decimal premium)
    {
        var holder = await SeedHolderEntityAsync(
            "Jane", "Doe", $"holder{_holderCounter++}@example.com");

        return await _policiesService.Create(new CreatePolicyDto
        {
            Type = Models.Enums.PolicyType.Auto,
            PolicyHolderId = holder.Id,
            Premium = premium,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2027, 1, 1)
        });
    }

    /// <summary>
    ///     Files a claim against a policy.
    /// </summary>
    /// <param name="policyId">The policy the claim is filed against.</param>
    /// <param name="amount">The claim amount.</param>
    /// <returns>The identifier of the created claim.</returns>
    private async Task<int> SeedClaimAsync(int policyId, decimal amount)
    {
        return await _claimsService.Create(new CreateClaimDto { PolicyId = policyId, Amount = amount });
    }

    /// <summary>
    ///     An untouched query object is the first page at the default size.
    /// </summary>
    [Fact]
    public async Task GetAll_DefaultQuery_ReturnsFirstPageOfDefaultSize()
    {
        await SeedHoldersAsync(30);

        var result = await _policyHoldersService.GetAll(new PaginationQuery());

        Assert.Equal(1, result.Page);
        Assert.Equal(PaginationQuery.DefaultPageSize, result.PageSize);
        Assert.Equal(25, result.PageSize);
        Assert.Equal(30, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(25, result.Items.Count);
        Assert.False(result.HasPrevious);
        Assert.True(result.HasNext);
    }

    /// <summary>
    ///     An explicit page and page size return exactly that slice, offset by the pages before it.
    /// </summary>
    [Fact]
    public async Task GetAll_ExplicitPageAndSize_ReturnsThatSlice()
    {
        await SeedHoldersAsync(30);

        var result = await _policyHoldersService.GetAll(new PaginationQuery { Page = 2, PageSize = 10 });

        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(30, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(10, result.Items.Count);
        Assert.True(result.HasPrevious);
        Assert.True(result.HasNext);

        // Identifiers run 1..30 in insertion order, so the second page of ten starts at 11.
        Assert.Equal(11, result.Items[0].Id);
        Assert.Equal(20, result.Items[9].Id);
    }

    /// <summary>
    ///     A page size above the ceiling is clamped down to it, not rejected.
    /// </summary>
    [Fact]
    public async Task GetAll_PageSizeAboveMax_IsClampedToMax()
    {
        await SeedHoldersAsync(30);

        var result = await _policyHoldersService.GetAll(new PaginationQuery { PageSize = 5000 });

        Assert.Equal(PaginationQuery.MaxPageSize, result.PageSize);
        Assert.Equal(30, result.Items.Count);
        Assert.Equal(1, result.TotalPages);
        Assert.False(result.HasNext);
    }

    /// <summary>
    ///     A page size of zero or less is clamped up to one item per page rather than dividing by
    ///     zero or returning the whole table.
    /// </summary>
    /// <param name="requestedPageSize">The out-of-range page size sent by the caller.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task GetAll_PageSizeBelowOne_IsClampedToOne(int requestedPageSize)
    {
        await SeedHoldersAsync(30);

        var result = await _policyHoldersService.GetAll(new PaginationQuery { PageSize = requestedPageSize });

        Assert.Equal(1, result.PageSize);
        Assert.Equal(1, result.Items.Count);
        Assert.Equal(30, result.TotalCount);
        Assert.Equal(30, result.TotalPages);
    }

    /// <summary>
    ///     A page number of zero or less is clamped to the first page, which has no previous page.
    /// </summary>
    /// <param name="requestedPage">The out-of-range page number sent by the caller.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task GetAll_PageBelowOne_IsClampedToFirstPage(int requestedPage)
    {
        await SeedHoldersAsync(30);

        var result = await _policyHoldersService.GetAll(new PaginationQuery { Page = requestedPage });

        Assert.Equal(1, result.Page);
        Assert.Equal(25, result.Items.Count);
        Assert.False(result.HasPrevious);
    }

    /// <summary>
    ///     The page count rounds up, so a partial final page is counted rather than dropped.
    /// </summary>
    [Fact]
    public async Task GetAll_TotalPages_RoundsUp()
    {
        await SeedHoldersAsync(10);

        var result = await _policyHoldersService.GetAll(new PaginationQuery { PageSize = 3 });

        Assert.Equal(10, result.TotalCount);
        Assert.Equal(4, result.TotalPages);
        Assert.Equal(3, result.Items.Count);
        Assert.True(result.HasNext);
    }

    /// <summary>
    ///     Nothing to page through means zero pages, an empty page and no next page.
    /// </summary>
    [Fact]
    public async Task GetAll_NoRows_TotalPagesIsZero()
    {
        var result = await _policyHoldersService.GetAll(new PaginationQuery());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasNext);
        Assert.False(result.HasPrevious);
    }

    /// <summary>
    ///     The first page has nothing before it and something after it.
    /// </summary>
    [Fact]
    public async Task GetAll_FirstPage_HasNoPreviousAndHasNext()
    {
        await SeedHoldersAsync(5);

        var result = await _policyHoldersService.GetAll(new PaginationQuery { Page = 1, PageSize = 2 });

        Assert.False(result.HasPrevious);
        Assert.True(result.HasNext);
    }

    /// <summary>
    ///     A page in the middle has both a previous and a next page.
    /// </summary>
    [Fact]
    public async Task GetAll_MiddlePage_HasPreviousAndHasNext()
    {
        await SeedHoldersAsync(5);

        var result = await _policyHoldersService.GetAll(new PaginationQuery { Page = 2, PageSize = 2 });

        Assert.True(result.HasPrevious);
        Assert.True(result.HasNext);
    }

    /// <summary>
    ///     The last page has nothing after it.
    /// </summary>
    [Fact]
    public async Task GetAll_LastPage_HasPreviousAndNoNext()
    {
        await SeedHoldersAsync(5);

        var result = await _policyHoldersService.GetAll(new PaginationQuery { Page = 3, PageSize = 2 });

        Assert.True(result.HasPrevious);
        Assert.False(result.HasNext);
    }

    /// <summary>
    ///     Sorting ascending on a non-default field reverses the default order for the seeded data.
    /// </summary>
    [Fact]
    public async Task GetAll_SortByLastNameAscending_OrdersByLastName()
    {
        await SeedNamedHolderAsync("First", "Zulu");
        await SeedNamedHolderAsync("Second", "Mike");
        await SeedNamedHolderAsync("Third", "Alpha");
        await SeedNamedHolderAsync("Fourth", "Yankee");

        var result = await _policyHoldersService.GetAll(new PaginationQuery { SortBy = "lastName" });

        Assert.Equal(new[] { "Alpha", "Mike", "Yankee", "Zulu" }, result.Items.Select(h => h.LastName));
        Assert.Equal(new[] { 3, 2, 4, 1 }, result.Items.Select(h => h.Id));
    }

    /// <summary>
    ///     Sorting descending on the same field is the exact reverse, which matches neither the
    ///     ascending order nor the default identifier order for this data.
    /// </summary>
    [Fact]
    public async Task GetAll_SortByLastNameDescending_ReversesOrder()
    {
        await SeedNamedHolderAsync("First", "Zulu");
        await SeedNamedHolderAsync("Second", "Mike");
        await SeedNamedHolderAsync("Third", "Alpha");
        await SeedNamedHolderAsync("Fourth", "Yankee");

        var result = await _policyHoldersService.GetAll(
            new PaginationQuery { SortBy = "lastName", Descending = true });

        Assert.Equal(new[] { "Zulu", "Yankee", "Mike", "Alpha" }, result.Items.Select(h => h.LastName));
        Assert.Equal(new[] { 1, 4, 2, 3 }, result.Items.Select(h => h.Id));
    }

    /// <summary>
    ///     The sort key is matched without regard to case, so a client can send <c>LastName</c>.
    /// </summary>
    [Fact]
    public async Task GetAll_SortBy_IsCaseInsensitive()
    {
        await SeedNamedHolderAsync("First", "Zulu");
        await SeedNamedHolderAsync("Second", "Mike");
        await SeedNamedHolderAsync("Third", "Alpha");

        var result = await _policyHoldersService.GetAll(new PaginationQuery { SortBy = "LastName" });

        Assert.Equal(new[] { "Alpha", "Mike", "Zulu" }, result.Items.Select(h => h.LastName));
    }

    /// <summary>
    ///     An unrecognised sort key falls back to the documented default, which is the same order an
    ///     absent key produces.
    /// </summary>
    [Fact]
    public async Task GetAll_UnknownSortKey_FallsBackToDefaultOrder()
    {
        await SeedNamedHolderAsync("First", "Zulu");
        await SeedNamedHolderAsync("Second", "Mike");
        await SeedNamedHolderAsync("Third", "Alpha");

        var unknown = await _policyHoldersService.GetAll(new PaginationQuery { SortBy = "notAColumn" });
        var absent = await _policyHoldersService.GetAll(new PaginationQuery { SortBy = null });

        Assert.Equal(new[] { 1, 2, 3 }, unknown.Items.Select(h => h.Id));
        Assert.Equal(absent.Items.Select(h => h.Id), unknown.Items.Select(h => h.Id));
    }

    /// <summary>
    ///     Claims sort on a non-identifier field too, and a page size smaller than the result set
    ///     takes the first slice of that order.
    /// </summary>
    [Fact]
    public async Task GetAll_Claims_SortsByAmountDescending_AndPages()
    {
        var policyId = await SeedPolicyAsync(500m);
        await SeedClaimAsync(policyId, 300m);
        await SeedClaimAsync(policyId, 100m);
        await SeedClaimAsync(policyId, 200m);

        var result = await _claimsService.GetAll(
            new PaginationQuery { SortBy = "amount", Descending = true, PageSize = 2 });

        Assert.Equal(new[] { 300m, 200m }, result.Items.Select(c => c.Amount));
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.False(result.HasPrevious);
        Assert.True(result.HasNext);
    }

    /// <summary>
    ///     The status filter and pagination compose: the count describes the filtered set only, and the
    ///     pages are sliced out of that set rather than out of the whole table.
    /// </summary>
    [Fact]
    public async Task GetAll_Policies_StatusFilterCombinedWithPagination()
    {
        var ten = await SeedPolicyAsync(10m);
        var twenty = await SeedPolicyAsync(20m);
        await SeedPolicyAsync(30m);
        var forty = await SeedPolicyAsync(40m);
        await SeedPolicyAsync(50m);

        await _policiesService.Cancel(twenty);
        await _policiesService.Cancel(forty);

        var query = new PaginationQuery { SortBy = "premium", Descending = true, PageSize = 2 };

        var first = await _policiesService.GetAll(query, PolicyStatus.Active);

        // The active premiums are 10, 30 and 50; descending, the first page of two is 50 and 30.
        Assert.Equal(new[] { 50m, 30m }, first.Items.Select(p => p.Premium));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.False(first.HasPrevious);
        Assert.True(first.HasNext);

        query.Page = 2;
        var second = await _policiesService.GetAll(query, PolicyStatus.Active);

        Assert.Equal(new[] { 10m }, second.Items.Select(p => p.Premium));
        Assert.Equal(ten, Assert.Single(second.Items).Id);
        Assert.Equal(3, second.TotalCount);
        Assert.True(second.HasPrevious);
        Assert.False(second.HasNext);

        // Together the two pages account for every active policy and nothing that was cancelled.
        Assert.Equal(
            new[] { 50m, 30m, 10m },
            first.Items.Concat(second.Items).Select(p => p.Premium));
    }

    /// <summary>
    ///     A page past the end is empty rather than an error, and still reports the true total so the
    ///     caller can see it has over-paged.
    /// </summary>
    [Fact]
    public async Task GetAll_PagePastEnd_ReturnsEmptyItemsWithUnchangedTotalCount()
    {
        await SeedHoldersAsync(5);

        var result = await _policyHoldersService.GetAll(new PaginationQuery { Page = 99, PageSize = 2 });

        Assert.Empty(result.Items);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(99, result.Page);
        Assert.True(result.HasPrevious);
        Assert.False(result.HasNext);
    }

    /// <summary>
    ///     The list path never touches the memory cache. The full-list entry is retired, so a read
    ///     must be neither served from nor written to the cache.
    /// </summary>
    [Fact]
    public async Task GetAll_DoesNotReadOrWriteTheCache()
    {
        await SeedHoldersAsync(5);

        var result = await _policyHoldersService.GetAll(new PaginationQuery { PageSize = 2 });

        Assert.Equal(2, result.Items.Count);
        _cache.VerifyNoOtherCalls();
    }

    /// <summary>
    ///     The envelope itself survives a page size of zero: it treats the page size as one instead of
    ///     dividing by zero, and reports zero pages for an empty result.
    /// </summary>
    [Fact]
    public void Create_NonPositivePageSize_DoesNotDivideByZero()
    {
        var empty = PagedResult<int>.Create(Array.Empty<int>(), 0, 1, 0);
        var lastPage = PagedResult<int>.Create(new[] { 1, 2, 3 }, 3, 3, 0);
        var severalPages = PagedResult<int>.Create(new[] { 1 }, 7, 1, 0);

        Assert.Equal(1, empty.PageSize);
        Assert.Equal(0, empty.TotalPages);
        Assert.False(empty.HasNext);
        Assert.False(empty.HasPrevious);

        Assert.Equal(1, lastPage.PageSize);
        Assert.Equal(3, lastPage.TotalPages);
        Assert.True(lastPage.HasPrevious);
        Assert.False(lastPage.HasNext);

        Assert.Equal(1, severalPages.PageSize);
        Assert.Equal(7, severalPages.TotalPages);
        Assert.False(severalPages.HasPrevious);
        Assert.True(severalPages.HasNext);
    }
}

using Microsoft.EntityFrameworkCore;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Services;

/// <summary>
///     Unit tests for the ReportsService class.
/// </summary>
public class ReportsServiceTests : ServiceTestBase
{
    private readonly ReportsService _reportsService;
    private readonly PoliciesService _policiesService;
    private readonly ClaimsService _claimsService;
    private int _holderCounter;

    public ReportsServiceTests()
    {
        _reportsService = new ReportsService(Context);
        _policiesService = new PoliciesService(Context, Numbers);
        _claimsService = new ClaimsService(Context, Numbers);
    }

    /// <summary>
    ///     Creates a policyholder with a unique address, so the tests do not collide on the unique
    ///     index the provider happens to model.
    /// </summary>
    /// <param name="first">The first name.</param>
    /// <param name="last">The last name.</param>
    private async Task<int> SeedHolderAsync(string first = "Jane", string last = "Doe")
    {
        var holder = await SeedHolderEntityAsync(first, last, $"holder{_holderCounter++}@example.com");
        return holder.Id;
    }

    /// <summary>
    ///     Creates a policy for a holder.
    /// </summary>
    /// <param name="holderId">The owning policyholder.</param>
    /// <param name="premium">The policy premium.</param>
    /// <param name="type">The policy type.</param>
    /// <param name="coverageLimit">The policy's coverage limit.</param>
    private async Task<int> SeedPolicyAsync(
        int holderId,
        decimal premium,
        PolicyType type = PolicyType.Auto,
        decimal? coverageLimit = null)
    {
        return await _policiesService.Create(new CreatePolicyDto
        {
            PolicyHolderId = holderId,
            Premium = premium,
            Type = type,
            CoverageLimit = coverageLimit,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2027, 1, 1)
        });
    }

    /// <summary>
    ///     Files a claim against a policy and adjudicates it, so a report can be given a decision to
    ///     count or to leave out.
    /// </summary>
    /// <param name="policyId">The policy to claim against.</param>
    /// <param name="amount">The claim amount.</param>
    /// <param name="decision">The status to adjudicate to, or null to leave the claim pending.</param>
    private async Task<int> SeedClaimAsync(int policyId, decimal amount, ClaimStatus? decision = null)
    {
        var claimId = await _claimsService.Create(new CreateClaimDto { PolicyId = policyId, Amount = amount });

        if (decision is not null)
        {
            await _claimsService.UpdateStatus(
                claimId, new UpdateClaimStatusDto { Status = decision.Value });
        }

        return claimId;
    }

    /// <summary>
    ///     An empty book still reports a row per open status, so the shape of the report does not
    ///     depend on whether anything has been filed yet.
    /// </summary>
    [Fact]
    public async Task GetOpenClaimsByStatus_NoClaims_ReportsEveryOpenStatusAtZero()
    {
        var report = await _reportsService.GetOpenClaimsByStatus();

        Assert.Equal(new[] { ClaimStatus.Pending, ClaimStatus.Approved }, report.Statuses.Select(s => s.Status));
        Assert.All(report.Statuses, s =>
        {
            Assert.Equal(0, s.ClaimCount);
            Assert.Equal(0m, s.TotalAmount);
            Assert.Equal(0m, s.AverageAmount);
        });
        Assert.Equal(0, report.TotalOpenClaims);
        Assert.Equal(0m, report.TotalOpenAmount);
    }

    /// <summary>
    ///     Pending and approved claims are counted and totalled; denied claims are not open and are
    ///     left out entirely.
    /// </summary>
    [Fact]
    public async Task GetOpenClaimsByStatus_CountsOpenClaimsAndExcludesDeniedOnes()
    {
        var holder = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holder, 1000m, coverageLimit: 2000m);

        await SeedClaimAsync(policyId, 100m);
        await SeedClaimAsync(policyId, 300m);
        await SeedClaimAsync(policyId, 200m, ClaimStatus.Approved);
        await SeedClaimAsync(policyId, 999m, ClaimStatus.Denied);

        var report = await _reportsService.GetOpenClaimsByStatus();

        var pending = report.Statuses.Single(s => s.Status == ClaimStatus.Pending);
        Assert.Equal(2, pending.ClaimCount);
        Assert.Equal(400m, pending.TotalAmount);
        Assert.Equal(200m, pending.AverageAmount);

        var approved = report.Statuses.Single(s => s.Status == ClaimStatus.Approved);
        Assert.Equal(1, approved.ClaimCount);
        Assert.Equal(200m, approved.TotalAmount);

        Assert.Equal(3, report.TotalOpenClaims);
        Assert.Equal(600m, report.TotalOpenAmount);
        Assert.DoesNotContain(report.Statuses, s => s.Status == ClaimStatus.Denied);
    }

    /// <summary>
    ///     An empty book still reports a row per policy type, so the shape of the report does not
    ///     depend on which lines of business have been sold.
    /// </summary>
    [Fact]
    public async Task GetPremiumByType_NoPolicies_ReportsEveryTypeAtZero()
    {
        var report = await _reportsService.GetPremiumByType();

        Assert.Equal(Enum.GetValues<PolicyType>(), report.Types.Select(t => t.Type));
        Assert.All(report.Types, t =>
        {
            Assert.Equal(0, t.PolicyCount);
            Assert.Equal(0m, t.TotalPremium);
            Assert.Equal(0m, t.AveragePremium);
        });
        Assert.Equal(0, report.TotalPolicies);
        Assert.Equal(0m, report.TotalPremium);
    }

    /// <summary>
    ///     Premium is totalled per policy type, with the policy count and the average alongside it.
    /// </summary>
    [Fact]
    public async Task GetPremiumByType_TotalsPremiumPerType()
    {
        var holder = await SeedHolderAsync();

        await SeedPolicyAsync(holder, 100m, PolicyType.Auto);
        await SeedPolicyAsync(holder, 300m, PolicyType.Auto);
        await SeedPolicyAsync(holder, 250m, PolicyType.Home);
        await SeedPolicyAsync(holder, 400m, PolicyType.Life);

        var report = await _reportsService.GetPremiumByType();

        var auto = report.Types.Single(t => t.Type == PolicyType.Auto);
        Assert.Equal(2, auto.PolicyCount);
        Assert.Equal(400m, auto.TotalPremium);
        Assert.Equal(200m, auto.AveragePremium);

        Assert.Equal(250m, report.Types.Single(t => t.Type == PolicyType.Home).TotalPremium);
        Assert.Equal(400m, report.Types.Single(t => t.Type == PolicyType.Life).TotalPremium);

        Assert.Equal(4, report.TotalPolicies);
        Assert.Equal(1050m, report.TotalPremium);
    }

    /// <summary>
    ///     A cancelled policy still contributed premium, and the report counts it: the figure answers
    ///     what was written, not what is still in force.
    /// </summary>
    [Fact]
    public async Task GetPremiumByType_CountsCancelledPolicies()
    {
        var holder = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holder, 500m);
        await SeedPolicyAsync(holder, 100m);

        await _policiesService.Cancel(policyId);

        var report = await _reportsService.GetPremiumByType();

        var auto = report.Types.Single(t => t.Type == PolicyType.Auto);
        Assert.Equal(2, auto.PolicyCount);
        Assert.Equal(600m, auto.TotalPremium);
    }

    /// <summary>
    ///     The ratio weighs a holder's open claim amount against their premium, and the default order
    ///     puts the heaviest holder first.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_OrdersByRatioDescending()
    {
        var light = await SeedHolderAsync("Ann", "Light");
        await SeedPolicyAsync(light, 1000m);
        await SeedClaimAsync((await Context.Policies.Select(p => p.Id).FirstAsync()), 100m);

        var heavy = await SeedHolderAsync("Bob", "Heavy");
        await SeedPolicyAsync(heavy, 1000m);
        await SeedClaimAsync((await Context.Policies.Where(p => p.PolicyHolderId == heavy)
            .Select(p => p.Id).FirstAsync()), 900m);

        var result = await _reportsService.GetClaimsRatioPerHolder(new PaginationQuery());

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { heavy, light }, result.Items.Select(r => r.PolicyHolderId));
        Assert.Equal(0.9m, result.Items[0].ClaimsRatio);
        Assert.Equal(900m, result.Items[0].TotalOpenClaimAmount);
        Assert.Equal(1, result.Items[0].OpenClaimCount);
        Assert.Equal(0.1m, result.Items[1].ClaimsRatio);
    }

    /// <summary>
    ///     A holder's ratio is built from every one of their policies, not just one.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_SumsEveryPolicyTheHolderOwns()
    {
        var holder = await SeedHolderAsync("Cara", "Combined");
        var first = await SeedPolicyAsync(holder, 100m);
        var second = await SeedPolicyAsync(holder, 300m, coverageLimit: 1000m);

        await SeedClaimAsync(first, 50m);
        await SeedClaimAsync(second, 350m);

        var row = Assert.Single((await _reportsService.GetClaimsRatioPerHolder(new PaginationQuery())).Items);

        Assert.Equal(2, row.PolicyCount);
        Assert.Equal(400m, row.TotalPremium);
        Assert.Equal(2, row.OpenClaimCount);
        Assert.Equal(400m, row.TotalOpenClaimAmount);
        Assert.Equal(1m, row.ClaimsRatio);
    }

    /// <summary>
    ///     A denied claim is not open, so it does not count against its holder.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_ExcludesDeniedClaims()
    {
        var holder = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holder, 1000m, coverageLimit: 5000m);

        await SeedClaimAsync(policyId, 200m, ClaimStatus.Denied);

        var row = Assert.Single((await _reportsService.GetClaimsRatioPerHolder(new PaginationQuery())).Items);

        Assert.Equal(0, row.OpenClaimCount);
        Assert.Equal(0m, row.TotalOpenClaimAmount);
        Assert.Equal(0m, row.ClaimsRatio);
    }

    /// <summary>
    ///     An approved claim counts against its holder: money that has been agreed to leave the
    ///     insurer.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_CountsApprovedClaims()
    {
        var holder = await SeedHolderAsync();
        var policyId = await SeedPolicyAsync(holder, 1000m, coverageLimit: 5000m);

        await SeedClaimAsync(policyId, 250m, ClaimStatus.Approved);

        var row = Assert.Single((await _reportsService.GetClaimsRatioPerHolder(new PaginationQuery())).Items);

        Assert.Equal(1, row.OpenClaimCount);
        Assert.Equal(250m, row.TotalOpenClaimAmount);
        Assert.Equal(0.25m, row.ClaimsRatio);
    }

    /// <summary>
    ///     A holder with policies but no claims has a ratio of zero, not a missing row.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_HolderWithoutClaims_ReportsZero()
    {
        var holder = await SeedHolderAsync();
        await SeedPolicyAsync(holder, 750m);

        var row = Assert.Single((await _reportsService.GetClaimsRatioPerHolder(new PaginationQuery())).Items);

        Assert.Equal(0m, row.ClaimsRatio);
        Assert.Equal(0, row.OpenClaimCount);
        Assert.Equal("Jane Doe", row.PolicyholderName);
    }

    /// <summary>
    ///     A holder with no policy is absent: there is no premium to weigh their claims against, and a
    ///     row of zeros for every such holder would bury the ones that matter.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_HolderWithoutPolicies_IsAbsent()
    {
        await SeedHolderAsync("Nobody", "Here");
        var withPolicy = await SeedHolderAsync("Somebody", "There");
        await SeedPolicyAsync(withPolicy, 100m);

        var result = await _reportsService.GetClaimsRatioPerHolder(new PaginationQuery());

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(withPolicy, Assert.Single(result.Items).PolicyHolderId);
    }

    /// <summary>
    ///     The report pages the holders it has, with the total scoped to them.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_PagesTheHolders()
    {
        for (var i = 0; i < 3; i++)
        {
            var holder = await SeedHolderAsync($"Holder{i}", $"Last{i}");
            await SeedPolicyAsync(holder, 1000m);
        }

        var result = await _reportsService.GetClaimsRatioPerHolder(new PaginationQuery { PageSize = 2 });

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.HasNext);

        // Three rows at two per page is two pages, so the second is the short one.
        result = await _reportsService.GetClaimsRatioPerHolder(
            new PaginationQuery { PageSize = 2, Page = 2 });

        Assert.Single(result.Items);
        Assert.True(result.HasPrevious);
        Assert.False(result.HasNext);
    }

    /// <summary>
    ///     A page size above the ceiling is clamped, and an unknown sort key falls back to the ratio.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_ClampsPageSizeAndFallsBackToTheRatioOrder()
    {
        var heavy = await SeedHolderAsync("Bob", "Heavy");
        await SeedPolicyAsync(heavy, 1000m, coverageLimit: 5000m);
        await SeedClaimAsync(await PolicyOfAsync(heavy), 800m);

        var light = await SeedHolderAsync("Ann", "Light");
        await SeedPolicyAsync(light, 1000m);
        await SeedClaimAsync(await PolicyOfAsync(light), 100m);

        var clamped = await _reportsService.GetClaimsRatioPerHolder(new PaginationQuery { PageSize = 5000 });
        var byUnknownKey = await _reportsService.GetClaimsRatioPerHolder(
            new PaginationQuery { SortBy = "notAColumn" });

        Assert.Equal(PaginationQuery.MaxPageSize, clamped.PageSize);
        Assert.Equal(
            clamped.Items.Select(r => r.PolicyHolderId),
            byUnknownKey.Items.Select(r => r.PolicyHolderId));
        Assert.Equal(heavy, byUnknownKey.Items[0].PolicyHolderId);
    }

    /// <summary>
    ///     Sorting on premium or on claim count reorders the report, and the identifier breaks ties.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_SortsOnTheOtherColumns()
    {
        var first = await SeedHolderAsync("Ann", "First");
        await SeedPolicyAsync(first, 100m);

        var second = await SeedHolderAsync("Bob", "Second");
        await SeedPolicyAsync(second, 900m);
        await SeedPolicyAsync(second, 900m);

        var byPremium = await _reportsService.GetClaimsRatioPerHolder(
            new PaginationQuery { SortBy = "totalPremium", Descending = true });

        Assert.Equal(new[] { second, first }, byPremium.Items.Select(r => r.PolicyHolderId));

        var byId = await _reportsService.GetClaimsRatioPerHolder(
            new PaginationQuery { SortBy = "policyHolderId" });

        Assert.Equal(new[] { first, second }, byId.Items.Select(r => r.PolicyHolderId));
    }

    /// <summary>
    ///     The default order is the ratio descending, because the report exists to surface the
    ///     heaviest holders first; <c>?descending=true</c> reverses it.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_DescendingReversesTheDefaultOrder()
    {
        var heavy = await SeedHolderAsync("Bob", "Heavy");
        await SeedPolicyAsync(heavy, 1000m, coverageLimit: 5000m);
        await SeedClaimAsync(await PolicyOfAsync(heavy), 800m);

        var light = await SeedHolderAsync("Ann", "Light");
        await SeedPolicyAsync(light, 1000m);
        await SeedClaimAsync(await PolicyOfAsync(light), 100m);

        var worstFirst = await _reportsService.GetClaimsRatioPerHolder(new PaginationQuery());
        var reversed = await _reportsService.GetClaimsRatioPerHolder(
            new PaginationQuery { Descending = true });

        Assert.Equal(new[] { heavy, light }, worstFirst.Items.Select(r => r.PolicyHolderId));
        Assert.Equal(new[] { light, heavy }, reversed.Items.Select(r => r.PolicyHolderId));
    }

    /// <summary>
    ///     Reads the identifier of the only policy a holder owns.
    /// </summary>
    /// <param name="holderId">The policyholder whose policy is wanted.</param>
    private async Task<int> PolicyOfAsync(int holderId)
        => await Context.Policies.Where(p => p.PolicyHolderId == holderId).Select(p => p.Id).SingleAsync();
}

using System.Net;
using System.Net.Http.Json;
using PolicyManager.DTOs;
using PolicyManager.Models.Enums;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Controllers;

/// <summary>
///     Tests for the reporting endpoints.
/// </summary>
public class ReportsControllerTests : ApiIntegrationTestBase
{
    /// <summary>
    ///     The open-claims report on an empty book still carries a row per open status.
    /// </summary>
    [Fact]
    public async Task GetOpenClaimsByStatus_OnAnEmptyBook_ReportsEveryOpenStatus()
    {
        var res = await Client.GetAsync("/api/reports/open-claims-by-status");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var report = await res.Content.ReadFromJsonAsync<OpenClaimsByStatusReportDto>();
        Assert.NotNull(report);
        Assert.Equal(
            new[] { ClaimStatus.Pending, ClaimStatus.Approved },
            report!.Statuses.Select(s => s.Status));
        Assert.Equal(0, report.TotalOpenClaims);
        Assert.Equal(0m, report.TotalOpenAmount);
    }

    /// <summary>
    ///     The open-claims report counts pending and approved claims and leaves denied ones out.
    /// </summary>
    [Fact]
    public async Task GetOpenClaimsByStatus_CountsOpenClaimsAndExcludesDeniedOnes()
    {
        var policyId = await SeedPolicyForNewHolderAsync();
        var denied = await SeedClaimAsync(policyId, 700m);
        await SeedClaimAsync(policyId, 100m);
        await SeedClaimAsync(policyId, 300m);
        var approved = await SeedClaimAsync(policyId, 200m);

        await Client.PatchAsJsonAsync($"/api/claims/{approved}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Approved });
        await Client.PatchAsJsonAsync($"/api/claims/{denied}/status",
            new UpdateClaimStatusDto { Status = ClaimStatus.Denied });

        var res = await Client.GetAsync("/api/reports/open-claims-by-status");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var report = await res.Content.ReadFromJsonAsync<OpenClaimsByStatusReportDto>();
        Assert.NotNull(report);

        var pending = report!.Statuses.Single(s => s.Status == ClaimStatus.Pending);
        Assert.Equal(2, pending.ClaimCount);
        Assert.Equal(400m, pending.TotalAmount);

        var approvedRow = report.Statuses.Single(s => s.Status == ClaimStatus.Approved);
        Assert.Equal(1, approvedRow.ClaimCount);
        Assert.Equal(200m, approvedRow.TotalAmount);

        Assert.Equal(3, report.TotalOpenClaims);
        Assert.Equal(600m, report.TotalOpenAmount);
        Assert.DoesNotContain(report.Statuses, s => s.Status == ClaimStatus.Denied);
    }

    /// <summary>
    ///     The premium report totals the book by policy type.
    /// </summary>
    [Fact]
    public async Task GetPremiumByType_TotalsTheBookByType()
    {
        var holderId = await SeedHolderAsync();
        await SeedPolicyAsync(holderId, 100m);
        await SeedPolicyAsync(holderId, 300m);

        var res = await Client.GetAsync("/api/reports/premium-by-type");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var report = await res.Content.ReadFromJsonAsync<PremiumByTypeReportDto>();
        Assert.NotNull(report);

        var auto = report!.Types.Single(t => t.Type == PolicyType.Auto);
        Assert.Equal(2, auto.PolicyCount);
        Assert.Equal(400m, auto.TotalPremium);
        Assert.Equal(200m, auto.AveragePremium);

        // The types nobody has bought are still reported, at zero.
        Assert.Equal(0m, report.Types.Single(t => t.Type == PolicyType.Life).TotalPremium);
        Assert.Equal(2, report.TotalPolicies);
        Assert.Equal(400m, report.TotalPremium);
    }

    /// <summary>
    ///     The claims-ratio report returns one row per holder who owns a policy, worst first.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_WeighsClaimsAgainstPremium()
    {
        var light = await SeedHolderAsync(new CreatePolicyHolderDto
        {
            FirstName = "Ann", LastName = "Light", Email = "ann@example.com"
        });
        await SeedPolicyAsync(light, 1000m);
        await SeedClaimAsync((await PolicyOfAsync(light)), 100m);

        var heavy = await SeedHolderAsync(new CreatePolicyHolderDto
        {
            FirstName = "Bob", LastName = "Heavy", Email = "bob@example.com"
        });
        await SeedPolicyAsync(heavy, 1000m);
        await SeedClaimAsync((await PolicyOfAsync(heavy)), 900m);

        var res = await Client.GetAsync("/api/reports/claims-ratio-per-holder");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var page = await res.Content.ReadFromJsonAsync<PagedResult<HolderClaimsRatioDto>>();
        Assert.NotNull(page);
        Assert.Equal(2, page!.TotalCount);

        Assert.Equal(heavy, page.Items[0].PolicyHolderId);
        Assert.Equal(0.9m, page.Items[0].ClaimsRatio);
        Assert.Equal(900m, page.Items[0].TotalOpenClaimAmount);
        Assert.Equal("Bob Heavy", page.Items[0].PolicyholderName);

        Assert.Equal(light, page.Items[1].PolicyHolderId);
        Assert.Equal(0.1m, page.Items[1].ClaimsRatio);
    }

    /// <summary>
    ///     The ratio report pages like every other list endpoint.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_PagesTheHolders()
    {
        var first = await SeedHolderAsync();
        await SeedPolicyAsync(first, 100m);

        var second = await SeedHolderAsync(new CreatePolicyHolderDto
        {
            FirstName = "Second", LastName = "Holder", Email = "second@example.com"
        });
        await SeedPolicyAsync(second, 200m);

        var third = await SeedHolderAsync(new CreatePolicyHolderDto
        {
            FirstName = "Third", LastName = "Holder", Email = "third@example.com"
        });
        await SeedPolicyAsync(third, 300m);

        var res = await Client.GetAsync("/api/reports/claims-ratio-per-holder?page=2&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var page = await res.Content.ReadFromJsonAsync<PagedResult<HolderClaimsRatioDto>>();
        Assert.NotNull(page);
        Assert.Equal(3, page!.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasPrevious);
        Assert.False(page.HasNext);

        var thirdRow = Assert.Single(page.Items);
        Assert.Equal(third, thirdRow.PolicyHolderId);
    }

    /// <summary>
    ///     A policyholder who owns no policy has no ratio and is left out of the report.
    /// </summary>
    [Fact]
    public async Task GetClaimsRatioPerHolder_HolderWithoutPolicies_IsNotReported()
    {
        await SeedHolderAsync();

        var res = await Client.GetAsync("/api/reports/claims-ratio-per-holder");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var page = await res.Content.ReadFromJsonAsync<PagedResult<HolderClaimsRatioDto>>();
        Assert.NotNull(page);
        Assert.Empty(page!.Items);
        Assert.Equal(0, page.TotalCount);
    }

    /// <summary>
    ///     A holder's single policy is read back so a claim can be filed against it, since the
    ///     seeding helpers create a policy but do not return which one when a holder owns more.
    /// </summary>
    /// <param name="holderId">The policyholder whose only policy is wanted.</param>
    private async Task<int> PolicyOfAsync(int holderId)
    {
        var holder = await (await Client.GetAsync($"/api/policyholders/{holderId}/policies"))
            .Content.ReadFromJsonAsync<PagedResult<PolicyDto>>();

        return Assert.Single(holder!.Items).Id;
    }
}

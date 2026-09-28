using PolicyManager.Domain;
using PolicyManager.Exceptions;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Tests.Domain;

/// <summary>
///     Tests for the rules governing whether a claim may be filed against a policy.
/// </summary>
public class ClaimRulesTests
{
    /// <summary>
    ///     An active policy accepts claims.
    /// </summary>
    [Fact]
    public void An_active_policy_accepts_claims()
    {
        var policy = ActivePolicy();

        ClaimRules.EnsurePolicyAcceptsClaims(policy);
    }

    /// <summary>
    ///     A cancelled policy does not, and the rule is named so a caller can act on it.
    /// </summary>
    [Theory]
    [InlineData(PolicyStatus.Cancelled)]
    [InlineData(PolicyStatus.Expired)]
    public void An_inactive_policy_rejects_claims(PolicyStatus status)
    {
        var policy = ActivePolicy();
        policy.Status = status;

        var error = Assert.Throws<BusinessRuleException>(() => ClaimRules.EnsurePolicyAcceptsClaims(policy));

        Assert.Equal(ClaimRules.PolicyNotActiveRule, error.Rule);
    }

    /// <summary>
    ///     A policy with no stated limit accepts any amount.
    /// </summary>
    /// <remarks>
    ///     Unlimited is a legitimate configuration. Treating a missing limit as zero would reject
    ///     every claim filed against an unconfigured policy.
    /// </remarks>
    [Fact]
    public void A_policy_with_no_coverage_limit_accepts_any_amount()
    {
        var policy = ActivePolicy();
        policy.CoverageLimit = null;

        ClaimRules.EnsureWithinCoverage(policy, 99_999_999m, 0m);
    }

    /// <summary>
    ///     A claim within the limit is accepted.
    /// </summary>
    [Fact]
    public void A_claim_within_the_limit_is_accepted()
    {
        var policy = ActivePolicy();
        policy.CoverageLimit = 1000m;

        ClaimRules.EnsureWithinCoverage(policy, 400m, 0m);
        ClaimRules.EnsureWithinCoverage(policy, 1000m, 0m);
    }

    /// <summary>
    ///     A single claim larger than the whole limit is rejected.
    /// </summary>
    [Fact]
    public void A_claim_larger_than_the_whole_limit_is_rejected()
    {
        var policy = ActivePolicy();
        policy.CoverageLimit = 1000m;

        var error = Assert.Throws<BusinessRuleException>(
            () => ClaimRules.EnsureWithinCoverage(policy, 1000.01m, 0m));

        Assert.Equal(ClaimRules.ExceedsCoverageRule, error.Rule);
    }

    /// <summary>
    ///     Individually valid claims that together exceed the limit are rejected.
    /// </summary>
    /// <remarks>
    ///     This is the case a single-amount check misses entirely: four claims of 300 against a
    ///     1000 limit each fit, and the fourth must be refused because only 100 remains.
    /// </remarks>
    [Fact]
    public void Claims_that_collectively_exceed_the_limit_are_rejected()
    {
        var policy = ActivePolicy();
        policy.CoverageLimit = 1000m;

        ClaimRules.EnsureWithinCoverage(policy, 300m, 0m);
        ClaimRules.EnsureWithinCoverage(policy, 300m, 300m);
        ClaimRules.EnsureWithinCoverage(policy, 300m, 600m);

        var error = Assert.Throws<BusinessRuleException>(
            () => ClaimRules.EnsureWithinCoverage(policy, 300m, 900m));

        Assert.Equal(ClaimRules.CoverageExhaustedRule, error.Rule);
    }

    /// <summary>
    ///     A claim fitting exactly into the remaining coverage is accepted, not refused off by a cent.
    /// </summary>
    [Fact]
    public void A_claim_filling_the_remaining_coverage_exactly_is_accepted()
    {
        var policy = ActivePolicy();
        policy.CoverageLimit = 1000m;

        ClaimRules.EnsureWithinCoverage(policy, 400m, 600m);
    }

    /// <summary>
    ///     A denied claim releases its coverage, because the amount was never payable.
    /// </summary>
    /// <remarks>
    ///     The service passes the sum of non-denied claims as <c>alreadyClaimed</c>; this documents
    ///     why a denial must be excluded rather than a service detail.
    /// </remarks>
    [Fact]
    public void Coverage_is_released_when_a_claim_is_denied()
    {
        var policy = ActivePolicy();
        policy.CoverageLimit = 1000m;

        // A pending claim of 1000 exhausts the limit.
        ClaimRules.EnsureWithinCoverage(policy, 1000m, 0m);

        // Once it is denied its amount no longer counts, so the limit is available again.
        var outstanding = 1000m - 1000m;
        ClaimRules.EnsureWithinCoverage(policy, 1000m, outstanding);
    }

    private static Policy ActivePolicy() => new()
    {
        Id = 1,
        PolicyNumber = "POL-0001",
        Status = PolicyStatus.Active,
        Type = PolicyType.Auto,
        Premium = 500m,
        StartDate = new DateTime(2026, 1, 1),
        EndDate = new DateTime(2027, 1, 1),
        PolicyHolderId = 1
    };
}

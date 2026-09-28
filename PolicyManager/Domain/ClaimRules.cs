using PolicyManager.Exceptions;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Domain;

/// <summary>
///     The domain rules that govern whether a claim may be filed against a policy.
/// </summary>
/// <remarks>
///     A deliberately small rule set — "engine-lite" — kept as pure functions over a loaded
///     <see cref="Policy" /> so a rule can be read, tested and reasoned about without a database, a
///     clock or a service. Each rule throws a <see cref="BusinessRuleException" /> naming the rule it
///     enforces, which the global handler turns into a 422 with that identifier in the body, so a
///     caller can distinguish "the policy is cancelled" from "you have exhausted the coverage".
///     <para>
///     Amount bounds are not re-checked here: the DTO already constrains a single claim to the
///     storable <c>decimal(10,2)</c> range, and duplicating the constant in two places guarantees
///     they drift.
///     </para>
/// </remarks>
public static class ClaimRules
{
    /// <summary>
    ///     Rule identifier reported when a claim is filed against a policy that is not active.
    /// </summary>
    public const string PolicyNotActiveRule = "policy-not-active";

    /// <summary>
    ///     Rule identifier reported when a single claim exceeds the policy's coverage limit.
    /// </summary>
    public const string ExceedsCoverageRule = "claim-exceeds-coverage";

    /// <summary>
    ///     Rule identifier reported when the policy's coverage is already exhausted.
    /// </summary>
    public const string CoverageExhaustedRule = "coverage-exhausted";

    /// <summary>
    ///     Ensures a policy is in a state that accepts new claims.
    /// </summary>
    /// <param name="policy">The policy the claim would be filed against.</param>
    /// <exception cref="BusinessRuleException">The policy is cancelled or expired.</exception>
    public static void EnsurePolicyAcceptsClaims(Policy policy)
    {
        if (policy.Status == PolicyStatus.Active) return;

        throw new BusinessRuleException(
            $"Policy {policy.PolicyNumber} is {policy.Status} and no longer accepts claims.",
            PolicyNotActiveRule);
    }

    /// <summary>
    ///     Ensures a claim fits within the policy's remaining coverage.
    /// </summary>
    /// <remarks>
    ///     Both a single claim and the aggregate are checked. Checking only the single amount lets a
    ///     caller file four claims that each fit under the limit and collectively exceed it, which is
    ///     the obvious way round a limit that only guards one of them.
    ///     <para>
    ///     A null <see cref="Policy.CoverageLimit" /> means the policy has no stated limit, so no
    ///     coverage rule applies. Unlimited is a legitimate configuration, and silently treating a
    ///     missing limit as zero would reject every claim against an unconfigured policy.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The policy the claim would be filed against.</param>
    /// <param name="amount">The amount being claimed.</param>
    /// <param name="alreadyClaimed">
    ///     The sum of the amounts of the policy's existing claims that are still recognised against
    ///     the limit.
    /// </param>
    /// <exception cref="BusinessRuleException">The claim exceeds the remaining coverage.</exception>
    public static void EnsureWithinCoverage(Policy policy, decimal amount, decimal alreadyClaimed)
    {
        if (policy.CoverageLimit is not { } limit) return;

        if (amount > limit)
        {
            throw new BusinessRuleException(
                $"Claim of {amount:0.00} exceeds the coverage limit of {limit:0.00} on policy {policy.PolicyNumber}.",
                ExceedsCoverageRule);
        }

        var remaining = limit - alreadyClaimed;
        if (amount > remaining)
        {
            throw new BusinessRuleException(
                $"Claim of {amount:0.00} exceeds the {remaining:0.00} of coverage remaining on policy {policy.PolicyNumber}.",
                CoverageExhaustedRule);
        }
    }
}

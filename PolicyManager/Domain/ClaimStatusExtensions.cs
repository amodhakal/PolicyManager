using PolicyManager.Models.Enums;

namespace PolicyManager.Domain;

/// <summary>
///     Classification of a claim by whether it is still open.
/// </summary>
/// <remarks>
///     A claim is open while it can still turn into money the insurer owes: pending it has not been
///     decided, approved it has been and the payout is outstanding. Only a denial closes it. The
///     same line decides which claims reserve a policy's coverage, so the two cannot drift apart.
/// </remarks>
public static class ClaimStatusExtensions
{
    /// <summary>
    ///     Whether a claim in this status is still open.
    /// </summary>
    /// <param name="status">The claim status to classify.</param>
    /// <returns>True for <see cref="ClaimStatus.Pending" /> and <see cref="ClaimStatus.Approved" />.</returns>
    public static bool IsOpen(this ClaimStatus status)
        => status is ClaimStatus.Pending or ClaimStatus.Approved;
}

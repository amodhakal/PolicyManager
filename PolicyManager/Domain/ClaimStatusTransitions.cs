using PolicyManager.Models.Enums;

namespace PolicyManager.Domain;

/// <summary>
///     The legal status transitions of a claim.
/// </summary>
/// <remarks>
///     <para>
///     Adjudication is a one-way door. <see cref="ClaimStatus.Pending" /> is the only status a claim
///     can move out of, and <see cref="ClaimStatus.Approved" /> and <see cref="ClaimStatus.Denied" />
///     are terminal. Without this, <c>PATCH /api/claims/{id}/status</c> accepted any pair of values,
///     so a denied claim could be flipped to approved and a paid claim silently retracted — the
///     caller had to know the rules because nothing enforced them.
///     </para>
///     <para>
///     Deliberately a closed set rather than a general state machine: there are three statuses, and
///     the transitions between them are a fact about the domain that will not grow quietly. When a
///     fourth status is added, this is the one place that has to be revisited, which is the point.
///     </para>
/// </remarks>
public static class ClaimStatusTransitions
{
    /// <summary>
    ///     The only transition out of each status.
    /// </summary>
    /// <remarks>
    ///     A status absent from this map is terminal and has no legal onward transition.
    /// </remarks>
    private static readonly Dictionary<ClaimStatus, ClaimStatus[]> Allowed = new()
    {
        [ClaimStatus.Pending] = [ClaimStatus.Approved, ClaimStatus.Denied]
    };

    /// <summary>
    ///     Determines whether a status may change to another.
    /// </summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The requested status.</param>
    /// <returns>True when the transition is legal.</returns>
    public static bool IsAllowed(ClaimStatus from, ClaimStatus to)
        => Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    /// <summary>
    ///     Describes every transition legal from a status, for use in an error message.
    /// </summary>
    /// <param name="from">The current status.</param>
    /// <returns>
    ///     A comma-separated list of permitted targets, or a statement that the status is final.
    /// </returns>
    public static string DescribeAllowed(ClaimStatus from)
        => Allowed.TryGetValue(from, out var targets)
            ? string.Join(", ", targets)
            : "none - this status is final";
}

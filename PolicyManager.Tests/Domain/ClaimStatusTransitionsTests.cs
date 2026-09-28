using PolicyManager.Domain;
using PolicyManager.Models.Enums;

namespace PolicyManager.Tests.Domain;

/// <summary>
///     Tests for the claim status state machine.
/// </summary>
/// <remarks>
///     The bug being pinned: <c>UpdateStatus</c> assigned whatever status it was given, so a denied
///     claim could be flipped back to approved and a paid claim silently retracted. Nothing about
///     the transition was checked, which left the caller to know the rules.
/// </remarks>
public class ClaimStatusTransitionsTests
{
    /// <summary>
    ///     A pending claim may be approved.
    /// </summary>
    [Fact]
    public void Pending_may_be_approved() =>
        Assert.True(ClaimStatusTransitions.IsAllowed(ClaimStatus.Pending, ClaimStatus.Approved));

    /// <summary>
    ///     A pending claim may be denied.
    /// </summary>
    [Fact]
    public void Pending_may_be_denied() =>
        Assert.True(ClaimStatusTransitions.IsAllowed(ClaimStatus.Pending, ClaimStatus.Denied));

    /// <summary>
    ///     Approved is terminal: the decision cannot be revisited.
    /// </summary>
    [Theory]
    [InlineData(ClaimStatus.Pending)]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Denied)]
    public void Approved_is_final(ClaimStatus target) =>
        Assert.False(ClaimStatusTransitions.IsAllowed(ClaimStatus.Approved, target));

    /// <summary>
    ///     Denied is terminal.
    /// </summary>
    [Theory]
    [InlineData(ClaimStatus.Pending)]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Denied)]
    public void Denied_is_final(ClaimStatus target) =>
        Assert.False(ClaimStatusTransitions.IsAllowed(ClaimStatus.Denied, target));

    /// <summary>
    ///     A status that does not change is not a transition, and is rejected rather than treated
    ///     as a successful no-op that stamps a new decision date.
    /// </summary>
    [Fact]
    public void A_no_op_transition_is_not_allowed()
    {
        Assert.False(ClaimStatusTransitions.IsAllowed(ClaimStatus.Pending, ClaimStatus.Pending));
        Assert.False(ClaimStatusTransitions.IsAllowed(ClaimStatus.Approved, ClaimStatus.Approved));
    }

    /// <summary>
    ///     The error message names the transitions that would have been legal.
    /// </summary>
    /// <remarks>
    ///     A caller who guessed wrong should not have to read the source to find out what is.
    /// </remarks>
    [Fact]
    public void A_terminal_status_explains_that_it_is_final()
    {
        Assert.Equal("none - this status is final", ClaimStatusTransitions.DescribeAllowed(ClaimStatus.Approved));
    }

    /// <summary>
    ///     A pending status lists its permitted targets.
    /// </summary>
    [Fact]
    public void A_pending_status_lists_its_permitted_targets()
    {
        var described = ClaimStatusTransitions.DescribeAllowed(ClaimStatus.Pending);

        Assert.Contains(nameof(ClaimStatus.Approved), described);
        Assert.Contains(nameof(ClaimStatus.Denied), described);
    }
}

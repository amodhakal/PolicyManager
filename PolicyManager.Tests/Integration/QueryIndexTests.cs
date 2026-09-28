using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PolicyManager.Data;
using PolicyManager.Models;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Pins the shape of the indexes, so a later change that quietly drops or reshapes one is caught
///     here rather than by a slow-query report.
/// </summary>
/// <remarks>
///     Asserts against the EF model rather than the database: the in-memory provider does not create
///     indexes at all, and a filtered index is part of the model, so the model is where the shape is
///     actually decided. The migration is what carries that shape to SQL Server, and the
///     <c>SqlServer</c>-tagged tests migrate for real.
/// </remarks>
public class QueryIndexTests
{
    private static readonly IEntityType Policy = Model().FindEntityType(typeof(Policy))!;
    private static readonly IEntityType PolicyHolder = Model().FindEntityType(typeof(PolicyHolder))!;
    private static readonly IEntityType Claim = Model().FindEntityType(typeof(Claim))!;
    private static readonly IEntityType Outbox = Model().FindEntityType(typeof(OutboxMessage))!;

    [Fact]
    public void A_policy_number_is_unique()
    {
        var index = Find(Policy, "IX_Policy_PolicyNumber");

        Assert.True(index.IsUnique, "A duplicate policy number must be a 409, not a silent second policy.");
        Assert.Equal([nameof(Models.Policy.PolicyNumber)], Columns(index));
    }

    [Fact]
    public void A_policyholder_email_is_unique()
    {
        // The constraint is over the keyed hash, not the address. The address is stored as
        // randomised ciphertext and so cannot be indexed or compared at all; the hash is
        // deterministic and safe to store beside it.
        var index = Find(PolicyHolder, "IX_PolicyHolder_EmailHash");

        // Indexed but not yet unique: the unique constraint is the second phase, applied once every
        // existing row has a blind index. See the PiiProtection migration for why it is two phases.
        Assert.Equal([nameof(Models.PolicyHolder.EmailHash)], Columns(index));
    }

    [Fact]
    public void The_encrypted_address_is_not_itself_indexed()
    {
        // An index over a randomised ciphertext would be both useless and enormous: the column
        // changes on every write, so every insert would update the index for no lookup benefit.
        Assert.Null(PolicyHolder.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Models.PolicyHolder.Email))));
    }

    [Fact]
    public void The_status_filter_is_indexed_with_the_page_tie_breaker()
    {
        // Status is the only filter the policies list supports, and every branch of its ORDER BY ends
        // in Id. Leading on Status makes the filtered COUNT and the filtered page seeks rather than
        // scans; the Id tail means the tie-breaker is already in index order.
        var index = Find(Policy, "IX_Policies_Status_Id");

        Assert.Equal([nameof(Models.Policy.Status), nameof(Models.Policy.Id)], Columns(index));
        Assert.Null(Filter(index));
    }

    [Fact]
    public void The_holder_scoped_policy_lookup_is_indexed_with_the_page_tie_breaker()
    {
        var index = Find(Policy, "IX_Policies_PolicyHolderId_Id");

        Assert.Equal([nameof(Models.Policy.PolicyHolderId), nameof(Models.Policy.Id)], Columns(index));
        Assert.Null(Filter(index));
    }

    [Fact]
    public void The_coverage_sum_is_served_by_a_filtered_index()
    {
        // Every claim is created after summing what its policy has already paid out, so this is the
        // hottest read in the system. Denied claims reserve nothing, so excluding them keeps denied
        // history out of the structure entirely.
        var index = Find(Claim, "IX_Claims_Coverage");

        Assert.Equal([nameof(Models.Claim.PolicyId), nameof(Models.Claim.Amount)], Columns(index));

        // Asserted literally: the predicate is the part that is easy to get wrong. Interpolating the
        // enum member produces "[Status] <> Denied", which SQL cannot resolve — and the failure only
        // surfaces when the migration runs against a real server.
        Assert.Equal("[Status] <> 2", Filter(index));
    }

    [Fact]
    public void The_recency_listing_is_indexed_with_the_page_tie_breaker()
    {
        var index = Find(Claim, "IX_Claims_FiledAt_Id");

        Assert.Equal([nameof(Models.Claim.FiledAt), nameof(Models.Claim.Id)], Columns(index));
    }

    [Fact]
    public void The_outbox_poll_is_indexed_on_the_column_it_orders_by()
    {
        // CreatedAt, not ProcessedAt. ProcessedAt is constant within the filter, so leading on it gave
        // the dispatcher an index that could not supply the CreatedAt ordering it asks for, and the
        // batch had to be sorted.
        var index = Find(Outbox, "IX_OutboxMessages_Pending");

        Assert.Equal([nameof(OutboxMessage.CreatedAt)], Columns(index));
        Assert.Equal("[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL", Filter(index));
    }

    [Fact]
    public void The_dispatcher_can_find_the_rows_it_just_claimed()
    {
        // LockToken was unindexed, so the read-back after claiming a batch scanned the whole table on
        // every pass that claimed anything. LockedUntil is the key so an expired lease is in the same
        // structure, and the filter keeps the index to rows actually held by some processor.
        var index = Find(Outbox, "IX_OutboxMessages_Claimed");

        Assert.Equal([nameof(OutboxMessage.LockedUntil)], Columns(index));
        Assert.Equal("[LockToken] IS NOT NULL", Filter(index));
    }

    [Theory]
    [InlineData("IX_Policies_Status")]
    [InlineData("IX_Policies_PolicyHolderId")]
    [InlineData("IX_Claims_PolicyId")]
    [InlineData("IX_OutboxMessages_ProcessedAt")]
    public void An_index_the_composites_superseded_is_gone(string name)
    {
        // Each was narrower than the composite that replaced it while costing a write on every
        // insert. Left in place they would be pure overhead. The two on Policies and Claims are the
        // foreign-key indexes, which EF recreates by convention, so removing them means removing the
        // explicit declaration and letting the composite satisfy the relationship.
        Assert.True(
            new[] { Policy, PolicyHolder, Claim, Outbox }
                .SelectMany(e => e.GetIndexes())
                .All(i => i.GetDatabaseName() != name),
            $"{name} should have been replaced by a composite index.");
    }

    private static string[] Columns(IIndex index) => [.. index.Properties.Select(p => p.Name)];

    private static string? Filter(IIndex index)
    {
        var filter = index.GetFilter();
        return string.IsNullOrEmpty(filter) ? null : filter;
    }

    private static IIndex Find(IEntityType entity, string name)
        => entity.GetIndexes().FirstOrDefault(i => i.GetDatabaseName() == name)
           ?? throw new InvalidOperationException(
               $"{entity.DisplayName()} has no index named {name}. Present: " +
               string.Join(", ", entity.GetIndexes().Select(i => i.GetDatabaseName())));

    private static IModel Model()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        return context.Model;
    }
}

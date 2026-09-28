using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.Models;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Proves that the rowversion token actually rejects a stale write.
/// </summary>
/// <remarks>
///     The in-memory provider ignores concurrency tokens, so the 409 cannot be observed there. This
///     runs against real SQL Server, which is what maintains the <c>rowversion</c> column, and is
///     tagged <c>Category=SqlServer</c> so it is excluded from runs without Docker.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public class ConcurrencyTokenTests(SqlServerFixture fixture) : SqlServerTestBase(fixture)
{
    [Fact]
    public async Task A_rowversion_is_assigned_by_the_database()
    {
        var holder = await SeedHolderAsync();

        Assert.Equal(8, holder.RowVersion.Length);
    }

    [Fact]
    public async Task A_write_against_a_stale_token_is_rejected()
    {
        var holder = await SeedHolderAsync();
        var staleToken = holder.RowVersion;

        // A second writer, loaded from the same database state the first writer saw.
        await using (var other = CreateContext())
        {
            var concurrent = await other.PolicyHolders.SingleAsync(h => h.Id == holder.Id);
            concurrent.LastName = "Changed by the other writer";
            await other.SaveChangesAsync();
        }

        // The first writer, still holding the token it read.
        await using var context = CreateContext();
        var tracked = await context.PolicyHolders.SingleAsync(h => h.Id == holder.Id);
        tracked.LastName = "Changed by the first writer";
        context.ApplyOriginalValue(tracked, Convert.ToBase64String(staleToken));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_write_against_the_current_token_succeeds_and_rotates_the_token()
    {
        var holder = await SeedHolderAsync();
        var currentToken = holder.RowVersion;

        await using var context = CreateContext();
        var tracked = await context.PolicyHolders.SingleAsync(h => h.Id == holder.Id);
        tracked.LastName = "Changed by the only writer";
        context.ApplyOriginalValue(tracked, Convert.ToBase64String(currentToken));
        await context.SaveChangesAsync();

        await using var verify = CreateContext();
        var reloaded = await verify.PolicyHolders.SingleAsync(h => h.Id == holder.Id);
        Assert.Equal("Changed by the only writer", reloaded.LastName);
        Assert.NotEqual(currentToken, reloaded.RowVersion);
    }

    private async Task<PolicyHolder> SeedHolderAsync()
    {
        await using var context = CreateContext();
        var holder = new PolicyHolder
        {
            FirstName = "Concurrent", LastName = "Writer", Email = $"cw{Guid.NewGuid():N}@test"
        };

        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        // Re-read so the returned row carries the rowversion the database generated rather than the
        // zero-length placeholder the client sent.
        return await context.PolicyHolders.AsNoTracking().SingleAsync(h => h.Id == holder.Id);
    }
}

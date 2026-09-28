using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PolicyManager.Data;
using PolicyManager.Models;
using PolicyManager.Services;

namespace PolicyManager.Tests.Services;

/// <summary>
///     Covers the audit stamping done by <see cref="AppDbContext" /> on every save.
/// </summary>
/// <remarks>
///     Driven with a fixed clock and a stubbed actor so the assertions cannot pass by coincidence
///     with a wall-clock value, and so the "who" half of the audit trail is verified independently of
///     whatever the host would resolve.
/// </remarks>
public class AuditingTests : IDisposable
{
    /// <summary>
    ///     An instant chosen to be unmistakable, so a default-valued assertion cannot pass.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2031, 4, 5, 6, 7, 8, TimeSpan.Zero);

    private readonly List<AppDbContext> _contexts = [];

    /// <summary>
    ///     One in-memory store per test, shared by every context the test opens, so a second context
    ///     observes what the first wrote. <see cref="CreateContext" /> mints a fresh name per call,
    ///     which is what keeps tests isolated from each other but would hide a cross-context write.
    /// </summary>
    private readonly string _database = Guid.NewGuid().ToString();

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Adding_a_policyholder_stamps_the_creation_fields()
    {
        await using var context = CreateContext(new StubCurrentUser("alice"));

        var holder = new PolicyHolder { FirstName = "Alice", LastName = "Smith", Email = "a@b.com" };
        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        Assert.Equal(Now.UtcDateTime, holder.CreatedAt);
        Assert.Equal("alice", holder.CreatedBy);
        Assert.Null(holder.UpdatedAt);
        Assert.Null(holder.UpdatedBy);
    }

    [Fact]
    public async Task Modifying_a_policyholder_stamps_the_update_fields_and_leaves_creation_alone()
    {
        await using var context = CreateContext(new StubCurrentUser("alice"));
        var holder = await SeedAsync(context, "alice");

        await using var later = CreateContext(new StubCurrentUser("bob"));
        var tracked = await later.PolicyHolders.SingleAsync(h => h.Id == holder.Id);
        tracked.LastName = "Jones";
        await later.SaveChangesAsync();

        Assert.Equal("bob", tracked.UpdatedBy);
        Assert.Equal(Now.UtcDateTime, tracked.UpdatedAt);
        Assert.Equal("alice", tracked.CreatedBy);
    }

    [Fact]
    public async Task A_save_with_no_authenticated_caller_is_attributed_to_the_system_token()
    {
        await using var context = CreateContext(new StubCurrentUser(HttpContextCurrentUser.SystemActor));

        var holder = new PolicyHolder { FirstName = "Anon", LastName = "Ymous", Email = "anon@b.com" };
        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        Assert.Equal(HttpContextCurrentUser.SystemActor, holder.CreatedBy);
    }

    [Fact]
    public async Task An_unattributed_context_uses_the_system_token()
    {
        // The convenience constructor used by tests and tooling resolves no ambient caller.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_database)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var context = new AppDbContext(options);
        _contexts.Add(context);

        var holder = new PolicyHolder { FirstName = "No", LastName = "Actor", Email = "na@b.com" };
        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        Assert.Equal(HttpContextCurrentUser.SystemActor, holder.CreatedBy);
    }

    [Fact]
    public async Task The_update_timestamp_survives_a_save_that_changes_only_the_audit_columns()
    {
        await using var context = CreateContext(new StubCurrentUser("alice"));
        var holder = await SeedAsync(context, "alice");

        holder.LastName = "Jones";
        await context.SaveChangesAsync();

        // Reassigning the same value produces no EF change for those properties, so without the
        // explicit IsModified in StampAuditFields the edit would be written without a fresh
        // timestamp and the audit trail would understate when the row last really changed.
        holder.LastName = "Jones";
        await context.SaveChangesAsync();

        Assert.Equal(Now.UtcDateTime, holder.UpdatedAt);
        Assert.Equal("alice", holder.UpdatedBy);
    }

    private static async Task<PolicyHolder> SeedAsync(AppDbContext context, string actor)
    {
        var holder = new PolicyHolder { FirstName = "Alice", LastName = "Smith", Email = "a@b.com" };
        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();
        return holder;
    }

    private AppDbContext CreateContext(ICurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_database)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new AppDbContext(options, new FixedTimeProvider(Now), currentUser);
        _contexts.Add(context);
        return context;
    }

    private sealed class StubCurrentUser(string actor) : ICurrentUser
    {
        public string Actor { get; } = actor;

        public IReadOnlyCollection<string> Roles { get; } = [];

        public bool IsInRole(string role) => false;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PolicyManager.Data;
using PolicyManager.Models;

namespace PolicyManager.Tests.Infrastructure;

/// <summary>
///     Base class for service (unit) tests, providing a per-test in-memory <see cref="AppDbContext" />.
/// </summary>
public abstract class ServiceTestBase : IDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ServiceTestBase" /> class with a fresh
    ///     in-memory database.
    /// </summary>
    protected ServiceTestBase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // The outbox helpers wrap the entity write and the message write in an explicit transaction. The in-memory
            // store has no transactions, so it raises this warning instead; it is expected here and must not throw.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Context = new AppDbContext(options);
    }

    /// <summary>
    ///     Gets the in-memory context under test.
    /// </summary>
    protected AppDbContext Context { get; }

    /// <summary>
    ///     Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Inserts a policyholder directly into the database.
    /// </summary>
    /// <param name="first">The first name.</param>
    /// <param name="last">The last name.</param>
    /// <param name="email">The email address.</param>
    /// <returns>The created policyholder entity.</returns>
    protected async Task<PolicyHolder> SeedHolderEntityAsync(
        string first = "Jane",
        string last = "Doe",
        string email = "jane@example.com")
    {
        var holder = new PolicyHolder { FirstName = first, LastName = last, Email = email };
        Context.PolicyHolders.Add(holder);
        await Context.SaveChangesAsync();
        return holder;
    }

    /// <summary>
    ///     Releases the resources owned by this instance.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing) Context.Dispose();
    }
}

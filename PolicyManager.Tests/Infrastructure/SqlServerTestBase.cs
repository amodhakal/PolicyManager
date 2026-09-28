using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolicyManager.Data;

namespace PolicyManager.Tests.Infrastructure;

/// <summary>
///     Base class for tests that need a real SQL Server rather than the in-memory provider.
/// </summary>
/// <remarks>
///     Deriving classes are marked <c>Category=SqlServer</c> so a run without Docker can exclude
///     them with <c>--filter Category!=SqlServer</c>. Each test gets a clean database by truncating
///     the shared tables before it runs.
/// </remarks>
[Trait("Category", "SqlServer")]
public abstract class SqlServerTestBase : IAsyncLifetime
{
    /// <summary>
    ///     Gets the shared container fixture, injected by the xUnit collection.
    /// </summary>
    protected SqlServerFixture Fixture { get; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqlServerTestBase" /> class.
    /// </summary>
    /// <param name="fixture">The shared container fixture.</param>
    protected SqlServerTestBase(SqlServerFixture fixture)
    {
        Fixture = fixture;
    }

    /// <summary>
    ///     Gets a context bound to the container.
    /// </summary>
    /// <returns>A new context. The caller owns it and should dispose it.</returns>
    protected AppDbContext CreateContext() => Fixture.CreateContext();

    /// <summary>
    ///     Empties the shared tables so the test starts from a known state.
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await SqlServerFixture.ResetAsync(context);
    }

    /// <summary>
    ///     No per-test resources to release.
    /// </summary>
    public virtual Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
///     xUnit collection that shares a single <see cref="SqlServerFixture" /> across every test class
///     that opts into it, so the container is started once per run instead of once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    /// <summary>
    ///     The collection name, referenced by <c>[Collection]</c> on participating test classes.
    /// </summary>
    public const string Name = "SqlServer";
}

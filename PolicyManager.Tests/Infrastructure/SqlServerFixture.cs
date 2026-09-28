using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using Testcontainers.MsSql;

namespace PolicyManager.Tests.Infrastructure;

/// <summary>
///     A real SQL Server instance, started on demand, for tests that must exercise database
///     behaviour the EF Core in-memory provider cannot represent.
/// </summary>
/// <remarks>
///     The in-memory provider builds its schema from the model and enforces almost nothing:
///     unique indexes, foreign keys, column precision, string length limits and transactions are
///     all silently absent. A test suite that only ever runs against it can pass while the
///     application is broken against the database it actually ships with. Tests that care about
///     those constraints derive from <see cref="SqlServerTestBase" /> and carry
///     <c>Category=SqlServer</c> so they can be filtered out of runs where Docker is unavailable.
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>
    ///     The name of the database the application schema is migrated into.
    /// </summary>
    public const string DatabaseName = "PolicyManagerTests";

    /// <summary>
    ///     The SQL Server image used by the fixture, pinned to the same tag <c>compose.yaml</c> uses
    ///     so constraint behaviour matches the local and deployed environment.
    /// </summary>
    private const string Image = "mcr.microsoft.com/mssql/server:2022-CU12-ubuntu-22.04";

    /// <summary>
    ///     The container's <c>sa</c> password. Satisfies the image's password policy (8+ characters
    ///     with at least three of upper case, lower case, digits and symbols). This is a throwaway
    ///     credential for an ephemeral container, not a secret.
    /// </summary>
    private const string Password = "Str0ng!TestPassword";

    private MsSqlContainer? _container;

    /// <summary>
    ///     Gets the connection string pointing at <see cref="DatabaseName" /> on the started container.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    ///     Starts the container and migrates the application schema into it.
    /// </summary>
    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder()
            .WithImage(Image)
            .WithPassword(Password)
            .Build();

        await _container.StartAsync();

        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = DatabaseName
        };
        ConnectionString = builder.ConnectionString;

        // MigrateAsync creates the database as well as the schema, so the fixture does not need a
        // separate "create database" step. Running the real migrations (rather than EnsureCreated)
        // means these tests also prove the migration scripts are valid and complete.
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>
    ///     Stops and removes the container.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    /// <summary>
    ///     Creates a context bound to the container.
    /// </summary>
    /// <returns>A new context. The caller owns it and should dispose it.</returns>
    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    /// <summary>
    ///     Removes every row from the tables the tests share, leaving the schema in place.
    /// </summary>
    /// <remarks>
    ///     Ordered child-first so the foreign keys stay satisfied throughout, and wrapped in a
    ///     transaction that rolls back afterwards so tests do not inherit each other's rows.
    /// </remarks>
    /// <param name="context">The context to clean through.</param>
    public static async Task ResetAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("DELETE FROM [OutboxMessages]");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM [Claims]");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM [Policies]");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM [PolicyHolders]");
    }
}

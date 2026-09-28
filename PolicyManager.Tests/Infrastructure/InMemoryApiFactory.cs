using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PolicyManager.Data;

namespace PolicyManager.Tests.Infrastructure;

/// <summary>
///     A <see cref="WebApplicationFactory{TEntryPoint}" /> that swaps the application's SQL Server
///     <see cref="AppDbContext" /> registration for a per-factory in-memory database.
/// </summary>
/// <remarks>
///     Fast and dependency-free, so it backs the bulk of the suite. It cannot represent unique
///     indexes, foreign keys, column precision or transactions; tests that need those run against
///     real SQL Server via <see cref="SqlServerApiFactory" /> instead.
/// </remarks>
public class InMemoryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    /// <summary>
    ///     Extra host configuration for the test that owns this factory, applied after the database
    ///     swap.
    /// </summary>
    /// <remarks>
    ///     A hook rather than a constructor argument because the useful adjustments are things only
    ///     the test knows: a configuration override such as a rate limit low enough to hit, or an
    ///     authentication scheme. Subclassing the factory per test would put each of those in its
    ///     own type.
    /// </remarks>
    public Action<IWebHostBuilder>? ConfigureHost { get; set; }

    /// <summary>
    ///     Removes the SQL Server context descriptors and registers an in-memory replacement.
    /// </summary>
    /// <param name="builder">The web host builder for the test server.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            TestHostServiceOverrides.RemoveAppDbContext(services);
            TestHostServiceOverrides.RemoveOutboxProcessor(services);

            services.AddDbContext<AppDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                // The outbox helpers wrap the entity write and the message write in an explicit
                // transaction. The in-memory store has no transactions, so it raises this warning
                // instead; it is expected here and must not throw.
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });

        ConfigureHost?.Invoke(builder);
    }
}

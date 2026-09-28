using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolicyManager.Data;

namespace PolicyManager.Tests.Infrastructure;

/// <summary>
///     A <see cref="WebApplicationFactory{TEntryPoint}" /> that points the application at a real SQL
///     Server container instead of substituting an in-memory store.
/// </summary>
/// <remarks>
///     Unlike <see cref="InMemoryApiFactory" /> this exercises the production service registrations,
///     the real schema created by the EF migrations, and SQL Server's own enforcement of unique
///     indexes, foreign keys, precision and string length limits. Use it for tests about behaviour
///     that only a relational database exhibits.
/// </remarks>
public class SqlServerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqlServerApiFactory" /> class.
    /// </summary>
    /// <param name="fixture">The shared container fixture supplying the connection string.</param>
    public SqlServerApiFactory(SqlServerFixture fixture)
    {
        _connectionString = fixture.ConnectionString;
    }

    /// <summary>
    ///     Removes the application's own context registration and rebinds it to the container, and
    ///     stops the outbox poller so it cannot drain rows a test is asserting on.
    /// </summary>
    /// <param name="builder">The web host builder for the test server.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            TestHostServiceOverrides.RemoveAppDbContext(services);
            TestHostServiceOverrides.RemoveOutboxProcessor(services);

            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(_connectionString));
        });
    }
}

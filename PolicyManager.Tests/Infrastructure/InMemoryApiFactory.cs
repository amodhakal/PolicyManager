using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolicyManager.Data;

namespace PolicyManager.Tests.Infrastructure;

/// <summary>
///     A <see cref="WebApplicationFactory{TEntryPoint}" /> that swaps the application's SQL Server
///     <see cref="AppDbContext" /> registration for a per-factory in-memory database.
/// </summary>
/// <remarks>
///     <see cref="Program" /> registers the context against a SQL Server connection string that is
///     not available during tests, so every descriptor that resolves the context has to be removed
///     before the in-memory registration is added. The removal is intentionally broad and lives here
///     so that its fragile matching logic exists in exactly one place.
/// </remarks>
public class InMemoryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    /// <summary>
    ///     Removes the SQL Server context descriptors and registers an in-memory replacement.
    /// </summary>
    /// <param name="builder">The web host builder for the test server.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                            || d.ServiceType == typeof(DbContext)
                            || d.ServiceType == typeof(AppDbContext)
                            || (d.ImplementationType?.Name.Contains("AppDbContext") ?? false)
                            || (d.ServiceType.FullName?.Contains("DbContextOptions") ?? false))
                .ToList();

            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}

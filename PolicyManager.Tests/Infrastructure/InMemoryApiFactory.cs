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
    ///     Signs requests with an admin token by default, so tests that are not about authorization
    ///     do not each have to arrange it.
    /// </summary>
    /// <remarks>
    ///     The token is real — signed with the key the host was configured with and validated by
    ///     the production pipeline — rather than a stubbed authentication handler. A stub would keep
    ///     the suite green if the signature, issuer, audience, lifetime or role claim were all wrong.
    ///     Set <c>false</c> to get an unauthenticated client, which is what the authorization tests
    ///     need in order to observe a 401.
    /// </remarks>
    public bool AuthenticateByDefault { get; set; } = true;

    /// <summary>
    ///     The roles the default client is authenticated with.
    /// </summary>
    public string[] DefaultRoles { get; set; } = [PolicyManager.Services.PolicyRoles.Admin];

    /// <summary>
    ///     The subject the default client's token identifies. It ends up in the audit columns, so
    ///     tests that assert on the actor set it deliberately.
    /// </summary>
    public string DefaultSubject { get; set; } = "test-user";

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

        // Always supplied, so a test host cannot come up with authentication unconfigured. The
        // application refuses to start in that state, which would otherwise turn every test into a
        // startup-failure test rather than a test of the thing under test.
        builder.AddTestAuthentication();

        ConfigureHost?.Invoke(builder);
    }

    /// <summary>
    ///     Creates a client, authenticated unless the factory says otherwise.
    /// </summary>
    /// <returns>A client for the test server.</returns>
    public new HttpClient CreateAuthenticatedClient()
        => AuthenticateByDefault
            ? this.CreateAuthenticatedClient(DefaultSubject, DefaultRoles)
            : CreateClient();
}
